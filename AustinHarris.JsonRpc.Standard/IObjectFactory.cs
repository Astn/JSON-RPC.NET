using System;

namespace AustinHarris.JsonRpc
{
    public interface IObjectFactory : IJsonRpcExceptionFactory, IJsonResponseFactory, IJsonRequestFactory
    {
        object DeserializeJson(string json, Type type);
        object DeserializeOrCoerceParameter(object parameter, string name, Type type);
    }
}