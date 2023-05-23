namespace TorchLiteRuntime
{
    // based on https://blog.slaks.net/2013-06-23/creating-immutable-stack/
    using System;

    /// <summary>
    /// Immutable stack.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    public abstract class ImmutableStack<T>
    {
        public static readonly ImmutableStack<T> Empty = new EmptyNode();

        public ImmutableStack<T> Push(T element)
        {
            return new LinkNode(this, element);
        }

        private class EmptyNode : ImmutableStack<T>
        {
            public override ImmutableStack<T> Pop()
            {
                throw new InvalidOperationException("Stack is empty");
            }

            public override T Peek()
            {
                throw new InvalidOperationException("Stack is empty");
            }

            public override bool IsEmpty { get { return true; } }

            public override string ToString() => string.Empty;
        }

        private class LinkNode : ImmutableStack<T>
        {
            private readonly ImmutableStack<T> previous;
            private readonly T element;
            private string toString;

            public LinkNode(ImmutableStack<T> previous, T element)
            {
                this.previous = previous;
                this.element = element;
                this.toString = previous.IsEmpty ? $"{element}" : $"{previous.ToString()}.{element}";
            }

            public override ImmutableStack<T> Pop()
            {
                if (this.toString.LastIndexOf('.') > 0)
                {
                    this.toString = this.toString.Substring(0, this.toString.LastIndexOf('.'));
                }
                return previous;
            }

            public override T Peek() { return element; }

            public override bool IsEmpty { get { return false; } }

            public override string ToString() => toString;
        }

        public abstract ImmutableStack<T> Pop();

        public abstract T Peek();

        public abstract bool IsEmpty { get; }

        public abstract string ToString();
    }
}
