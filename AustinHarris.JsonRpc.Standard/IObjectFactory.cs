using System;

namespace AustinHarris.JsonRpc
{
    public interface IObjectFactory : IJsonRpcExceptionFactory, IJsonResponseFactory, IJsonRequestFactory
    {
        object DeserializeJson(string json, Type type);
        void DeserializeJsonRef<T>(string json, ref ValueTuple<T> functionParameters, string[] functionParameterNames, Type[] functionParameterTypes);
    }
}