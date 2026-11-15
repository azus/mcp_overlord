using System.Collections;

namespace mcp_overlord.Logging
{
    /// <summary>
    /// A list that builds each line when it is read.
    /// A virtualized list box asks for the visible indexes, so a large payload does not become a string per line up front.
    /// </summary>
    public sealed class ComputedLineList : IList
    {
        private readonly Func<int, string> _line;

        /// <summary>
        /// Creates the list.
        /// </summary>
        /// <param name="count">Number of lines.</param>
        /// <param name="line">Builds the line at an index.</param>
        public ComputedLineList(int count, Func<int, string> line)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            Count = count;
            _line = line ?? throw new ArgumentNullException(nameof(line));
        }

        /// <inheritdoc />
        public int Count { get; }

        /// <inheritdoc />
        public bool IsReadOnly
        {
            get { return true; }
        }

        /// <inheritdoc />
        public bool IsFixedSize
        {
            get { return true; }
        }

        /// <inheritdoc />
        public object SyncRoot
        {
            get { return this; }
        }

        /// <inheritdoc />
        public bool IsSynchronized
        {
            get { return false; }
        }

        /// <inheritdoc />
        public object? this[int index]
        {
            get
            {
                if (index < 0 || index >= Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _line(index);
            }
            set
            {
                throw new NotSupportedException();
            }
        }

        /// <inheritdoc />
        public int Add(object? value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public void Clear()
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public bool Contains(object? value)
        {
            return IndexOf(value) >= 0;
        }

        /// <inheritdoc />
        public void CopyTo(Array array, int index)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            for (int line = 0; line < Count; line++)
            {
                array.SetValue(_line(line), index + line);
            }
        }

        /// <inheritdoc />
        public IEnumerator GetEnumerator()
        {
            for (int index = 0; index < Count; index++)
            {
                yield return _line(index);
            }
        }

        /// <inheritdoc />
        public int IndexOf(object? value)
        {
            if (value is not string text)
            {
                return -1;
            }

            for (int index = 0; index < Count; index++)
            {
                if (string.Equals(_line(index), text, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        /// <inheritdoc />
        public void Insert(int index, object? value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public void Remove(object? value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public void RemoveAt(int index)
        {
            throw new NotSupportedException();
        }
    }
}
