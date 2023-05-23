using System;
using System.Threading.Tasks;
using System.Linq;
using NSubstitute.Exceptions;
using NUnit.Framework;

namespace NSubstitute.Acceptance.Specs
{
    public class Issue573
    {
        public interface ISomeThing
        {
            event EventHandler<EventArgs> SomeEvent;
        }

        [Test]
        public void Raise_Event_Is_Thread_Safe()
        {
            /* Arrange */

            var thing = Substitute.For<ISomeThing>();

            void DummyEventHandler(object sender, EventArgs e)
            {
                /* do nothing */
            }

            /* Act */

            Task t = Task.Run(
                () =>
                {
                    for (int i = 0; i < 20; i++)
                    {
                        thing.SomeEvent += DummyEventHandler;
                        thing.SomeEvent -= DummyEventHandler;
                    }
                }
            );

            //Assert.DoesNotThrow(
            //    () =>
                {
                    for (int i = 0; i < 50; i++)
                    {
                        thing.SomeEvent += Raise.Event();
                    }
                }
            // );

            t.Wait();
        }
    }
}