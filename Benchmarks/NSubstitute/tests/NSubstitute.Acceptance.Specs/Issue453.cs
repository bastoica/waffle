using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute.Exceptions;
using NUnit.Framework;

namespace NSubstitute
{
    interface IThing { ISubThing SubThing { get; } }
    interface ISubThing { int SomeCall(int input); }

    [Parallelizable(ParallelScope.Children)]
    class Issue453
    {
        public IThing Act()
        {
            var thing = Substitute.For<IThing>();
            thing.SubThing.SomeCall(1);
            return thing;
        }

        [Test]
        [Repeat(1000)]
        public void Recieved1() => Act().SubThing.Received().SomeCall(1);

        [Test]
        [Repeat(1000)]
        public void DidNotRecieve2() => Act().SubThing.DidNotReceive().SomeCall(2);
    }
}
