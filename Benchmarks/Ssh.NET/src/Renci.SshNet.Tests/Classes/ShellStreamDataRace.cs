using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Renci.SshNet.Channels;
using Renci.SshNet.Common;
using Renci.SshNet.Tests.Common;

namespace Renci.SshNet.Tests.Classes
{
    /// <summary>
    /// Contains operation for working with SSH Shell.
    /// </summary>
    [TestClass]
    public class ShellStreamDataRace : TestBase
    {
        private Mock<ISession> _sessionMock;
        private Mock<IConnectionInfo> _connectionInfoMock;
        private Encoding _encoding;
        private string _terminalName;
        private uint _widthColumns;
        private uint _heightRows;
        private uint _widthPixels;
        private uint _heightPixels;
        private Dictionary<TerminalModes, uint> _terminalModes;
        private int _bufferSize;
        private Mock<IChannelSession> _channelSessionMock;

        protected override void OnInit()
        {
            base.OnInit();

            var random = new Random();
            _terminalName = random.Next().ToString(CultureInfo.InvariantCulture);
            _widthColumns = (uint)random.Next();
            _heightRows = (uint)random.Next();
            _widthPixels = (uint)random.Next();
            _heightPixels = (uint)random.Next();
            _terminalModes = new Dictionary<TerminalModes, uint>();
            _bufferSize = random.Next(100, 500);

            _encoding = Encoding.UTF8;
            _sessionMock = new Mock<ISession>(MockBehavior.Strict);
            _connectionInfoMock = new Mock<IConnectionInfo>(MockBehavior.Strict);
            _channelSessionMock = new Mock<IChannelSession>(MockBehavior.Strict);
        }

       
        [TestMethod] // issue #453
        public void Expect_Regex_RacesWithDispose()
        {
            // bash: /root/menu.sh: Отказан
            const string data1 = "bash: /root/menu.sh: \u041e\u0442\u043a\u0430\u0437\u0430\u043d";
            // о в доступе
            const string data2 = "\u043e \u0432 \u0434\u043e\u0441\u0442\u0443\u043f\u0435";
            // done
            const string data3 = "done";

            var shellStream = CreateShellStream();
            _channelSessionMock.Setup(t => t.Dispose());

            Task task = Task.Factory.StartNew(
                () =>
                {
                    _channelSessionMock.Raise(p => p.DataReceived += null,
                        new ChannelDataEventArgs(5, _encoding.GetBytes(data1)));
                    _channelSessionMock.Raise(p => p.DataReceived += null,
                        new ChannelDataEventArgs(5, _encoding.GetBytes(data2 + "\r\n")));
                    _channelSessionMock.Raise(p => p.DataReceived += null,
                        new ChannelDataEventArgs(5, _encoding.GetBytes(data3 + "\r\n")));

                    Thread.Sleep(1);
                    var output = shellStream.Expect("not there", TimeSpan.FromSeconds(1));
                }
            );

            Assert.AreEqual(data1 + data2, shellStream.ReadLine());
            Assert.AreEqual(data3, shellStream.ReadLine());

            Thread.Sleep(5);
            _channelSessionMock.Raise(p => p.Closed += null, this, new ChannelEventArgs(0));

            task.Wait();
        }

        private ShellStream CreateShellStream()
        {
            _sessionMock.Setup(p => p.ConnectionInfo).Returns(_connectionInfoMock.Object);
            _connectionInfoMock.Setup(p => p.Encoding).Returns(_encoding);
            _sessionMock.Setup(p => p.CreateChannelSession()).Returns(_channelSessionMock.Object);
            _channelSessionMock.Setup(p => p.Open());
            _channelSessionMock.Setup(p => p.SendPseudoTerminalRequest(_terminalName, _widthColumns, _heightRows,
                _widthPixels, _heightPixels, _terminalModes)).Returns(true);
            _channelSessionMock.Setup(p => p.SendShellRequest()).Returns(true);

            return new ShellStream(_sessionMock.Object,
                                   _terminalName,
                                   _widthColumns,
                                   _heightRows,
                                   _widthPixels,
                                   _heightPixels,
                                   _terminalModes,
                                   _bufferSize);
        }
    }
}