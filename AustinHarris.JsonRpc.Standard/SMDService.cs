using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace AustinHarris.JsonRpc
{
    public class SMD
    {
        public Dictionary<string, SMDService> Services { get; set; }

        public SMD ()
	    {
            Services = new Dictionary<string,SMDService>();
	    }

        internal void AddService(string method, Dictionary<string,Type> parameters, Dictionary<string, object> defaultValues, Delegate dele)
        {
            var newService = new SMDService(parameters, defaultValues, dele);
            Services.Add(method,newService);
        }
    }

    public class SMDService
    {
        public Delegate dele;

        public KeyValuePair<string, Type>[] Parameters { get; }

        /// <summary>
        /// Stores default values for optional parameters.
        /// </summary>
        public KeyValuePair<string, Object>[] DefaultValues { get; }
        public Type Returns { get; }

        /// <summary>
        /// Defines a service method http://dojotoolkit.org/reference-guide/1.8/dojox/rpc/smd.html
        /// </summary>
        /// <param name="parameters"></param>
        /// <param name="defaultValues"></param>
        public SMDService(Dictionary<string, Type> parameters, Dictionary<string, object> defaultValues, Delegate dele)
        {
            // TODO: Complete member initialization
            this.dele = dele;
            this.Parameters = parameters.Take(parameters.Count-1).ToArray(); // last param is return type similar to Func<,>

            // create the default values storage for optional parameters.
            this.DefaultValues = defaultValues.ToArray();

            // this is getting the return type from the end of the param list
            this.Returns = parameters.Values.LastOrDefault();

            _internalFunc = BuildInternalFunc();
        }

        private Func<IObjectFactory, string, (string,string)> BuildInternalFunc()
        {
            try
            {
                var plist = Parameters.Select(x => x.Value).ToArray();
                Type vtupType;
                if (plist.Length == 0) vtupType = typeof(ValueTuple);//.MakeGenericType(plist);
                else if (plist.Length == 1) vtupType = typeof(ValueTuple<>).MakeGenericType(plist);
                else if (plist.Length == 2) vtupType = typeof(ValueTuple<,>).MakeGenericType(plist);
                else if (plist.Length == 3) vtupType = typeof(ValueTuple<,,>).MakeGenericType(plist);
                else if (plist.Length == 4) vtupType = typeof(ValueTuple<,,,>).MakeGenericType(plist);
                else if (plist.Length == 5) vtupType = typeof(ValueTuple<,,,,>).MakeGenericType(plist);
                else if (plist.Length == 6) vtupType = typeof(ValueTuple<,,,,,>).MakeGenericType(plist);
                else if (plist.Length == 7) vtupType = typeof(ValueTuple<,,,,,,>).MakeGenericType(plist);
                else throw new NotImplementedException("Functions with more then 7 parameters are not supported");
                    // variables
                var id = Expression.Parameter(typeof(string), "id");
                var vtup = Expression.Parameter(vtupType, "vtup");
                var vmeta = Expression.Parameter(typeof(KeyValuePair<string, Type>[]),"vmeta");
                var vtarget = Expression.Parameter(dele.Target.GetType(), "vtarget");
                var vreturns = Expression.Parameter(Returns, "vreturns");
                var vserializedResult = Expression.Parameter(typeof(string), "vserializedResult");
                var vserializedResultAndId = Expression.Parameter(typeof((string, string)), "vserializedResultAndId");
                Expression<Func<KeyValuePair<string, Type>[]>> meta = () => Parameters;
                Expression<Func<object>> target = () => dele.Target;
                var meta2 = Expression.Constant(Parameters);
                var target2 = Expression.Constant(dele.Target);
                // vtupItems 
                var vtupItems = new Expression[Parameters.Length];
                for (int i = 0; i < vtupItems.Length; i++)
                {
                    vtupItems[i] = Expression.PropertyOrField(vtup, "Item" + (i + 1).ToString());
                }
                // functions
                var deser = typeof(IObjectFactory).GetMethods().First(x => {
                    return x.Name == "DeserializeJsonRef" &&
                    x.IsGenericMethod &&
                    x.GetGenericArguments().Length == plist.Length;
                }).MakeGenericMethod(plist);
                var serialize = typeof(IObjectFactory).GetMethods().First(x =>
                {
                    return x.Name == "Serialize" &&
                    x.IsGenericMethod &&
                    x.GetGenericArguments().Length == 1;
                }).MakeGenericMethod(Returns);

                // parameters
                var factory = Expression.Parameter(typeof(IObjectFactory), "factory");
                var json = Expression.Parameter(typeof(string), "json");

                BlockExpression setup = Expression.Block(
                    new[] { id , vmeta, vtarget, vtup, vreturns, vserializedResult, vserializedResultAndId },
                    Expression.Assign(id, Expression.Constant(String.Empty)),
                    Expression.Assign(vtup, Expression.New(vtupType)),
                    Expression.Assign(vmeta, meta2),
                    Expression.Assign(vtarget, Expression.Convert( target2, dele.Target.GetType())),
                    // call deserialize
                    Expression.Call(factory, deser, new Expression[] {
                                                                        json,
                                                                        vtup,
                                                                        id,
                                                                        vmeta
                                                                        }),
                    // call the jsonRpc function with the deserialized parameters
                    Expression.Assign(vreturns, Expression.Call(vtarget, dele.Method, vtupItems)),
                    // serialize the result of the jsonRpc function
                    Expression.Assign(vserializedResult, Expression.Call(factory, serialize, vreturns)),
                    Expression.Assign(Expression.PropertyOrField(vserializedResultAndId, "Item1"), vserializedResult),
                    Expression.Assign(Expression.PropertyOrField(vserializedResultAndId, "Item2"), id),
                    vserializedResultAndId
                    );
                
                var lambda = Expression.Lambda<Func<IObjectFactory, string, (string, string)>>(
                        setup,
                        new ParameterExpression[] { factory, json }
                        );

                Console.WriteLine(lambda.ToString());

                return lambda.Compile();
            }
            catch (Exception ex)
            {
                throw new Exception("parameters: " + Parameters.Length.ToString() +" :: " + String.Join(", ", Parameters.Select(x=> x.Key + " : " + x.Value.Name)), ex);
            }
        }

        Func<IObjectFactory, string,  (string,string)> _internalFunc;

        internal (string,string) Invoke(IObjectFactory objectFactory, string jsonRpc)
        {
            return _internalFunc(objectFactory, jsonRpc);
        }
    }
}
