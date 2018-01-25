using System;
using System.Collections.Generic;

namespace AustinHarris.JsonRpc
{
    public interface IObjectFactory : IJsonRpcExceptionFactory, IJsonRequestFactory
    {
        string Serialize<T>(T data);
        string ToJsonRpcResponse(ref InvokeResult response);
        object DeserializeJson(string json, Type type);
        string MethodName(string json);

        void DeserializeJsonRef<T>(string json, ref ValueTuple<T> functionParameters, ref string rawId, KeyValuePair<string,Type>[] functionParameterInfo);
        void DeserializeJsonRef<T1,T2>(string json, ref ValueTuple<T1, T2> functionParameters, ref string rawId, KeyValuePair<string, Type>[] functionParameterInfo);
        void DeserializeJsonRef<T1,T2,T3>(string json, ref ValueTuple<T1, T2, T3> functionParameters, ref string rawId, KeyValuePair<string, Type>[] functionParameterInfo);
        void DeserializeJsonRef<T1,T2,T3,T4>(string json, ref ValueTuple<T1, T2, T3, T4> functionParameters, ref string rawId, KeyValuePair<string, Type>[] functionParameterInfo);
        void DeserializeJsonRef<T1,T2,T3,T4,T5>(string json, ref ValueTuple<T1, T2, T3, T4, T5> functionParameters, ref string rawId, KeyValuePair<string, Type>[] functionParameterInfo);
        void DeserializeJsonRef<T1,T2,T3,T4,T5,T6>(string json, ref ValueTuple<T1, T2, T3, T4, T5, T6> functionParameters, ref string rawId, KeyValuePair<string, Type>[] functionParameterInfo);
        void DeserializeJsonRef<T1,T2,T3,T4,T5,T6,T7>(string json, ref ValueTuple<T1, T2, T3, T4, T5, T6, T7> functionParameters, ref string rawId, KeyValuePair<string, Type>[] functionParameterInfo);
    }
}