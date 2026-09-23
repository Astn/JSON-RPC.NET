using System.Buffers;
using Newtonsoft.Json;

namespace AustinHarris.JsonRpc.Newtonsoft
{
    /// <summary>Lets <see cref="JsonTextReader"/> / <see cref="JsonTextWriter"/> rent their char buffers from <see cref="ArrayPool{T}.Shared"/>.</summary>
    internal sealed class JsonArrayPool : IArrayPool<char>
    {
        public static readonly JsonArrayPool Instance = new JsonArrayPool();

        private JsonArrayPool() { }

        public char[] Rent(int minimumLength) => ArrayPool<char>.Shared.Rent(minimumLength);

        public void Return(char[] array)
        {
            if (array != null) ArrayPool<char>.Shared.Return(array);
        }
    }
}
