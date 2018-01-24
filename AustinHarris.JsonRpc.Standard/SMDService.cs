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

        public KeyValuePair<string, Type>[] parameters { get; }

        /// <summary>
        /// Stores default values for optional parameters.
        /// </summary>
        public KeyValuePair<string, Object>[] defaultValues { get; }
        public Type returns { get; }

        /// <summary>
        /// Defines a service method http://dojotoolkit.org/reference-guide/1.8/dojox/rpc/smd.html
        /// </summary>
        /// <param name="parameters"></param>
        /// <param name="defaultValues"></param>
        public SMDService(Dictionary<string, Type> parameters, Dictionary<string, object> defaultValues, Delegate dele)
        {
            // TODO: Complete member initialization
            this.dele = dele;
            this.parameters = parameters.Take(parameters.Count-1).ToArray(); // last param is return type similar to Func<,>

            // create the default values storage for optional parameters.
            this.defaultValues = defaultValues.ToArray();

            // this is getting the return type from the end of the param list
            this.returns = parameters.Values.LastOrDefault();

            _internalFunc = BuildInternalFunc();
        }

        private Func<IObjectFactory, string, object> BuildInternalFunc()
        {
            try
            {

            
            var plist = parameters.Select(x => x.Value).ToArray();
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
            var id = Expression.Parameter(typeof(object), "id");
            var vtup = Expression.Parameter(vtupType, "vtup");
            var vmeta = Expression.Parameter(typeof(KeyValuePair<string, Type>[]),"vmeta");
            var vtarget = Expression.Parameter(dele.Target.GetType(), "vtarget");
            Expression<Func<KeyValuePair<string, Type>[]>> meta = () => parameters;
            Expression<Func<object>> target = () => dele.Target;

            // vtupItems 
            var vtupItems = new Expression[parameters.Length];
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

            // parameters
            var factory = Expression.Parameter(typeof(IObjectFactory), "factory");
            var json = Expression.Parameter(typeof(string), "json");

            BlockExpression setup = Expression.Block(
                new[] { id , vmeta, vtarget, vtup },
                Expression.Assign(id, Expression.Constant((object)null)),
                Expression.Assign(vtup, Expression.New(vtupType)),
                Expression.Assign(vmeta, Expression.Invoke( meta )),
                Expression.Assign(vtarget, Expression.Convert( Expression.Invoke( target ), dele.Target.GetType())),
                Expression.Call(factory, deser, new Expression[] {
                                                                  json,
                                                                  vtup,
                                                                  id,
                                                                  vmeta
                                                                 }),
                Expression.Convert(Expression.Call(vtarget, dele.Method, vtupItems),typeof(object))
                );



            var lambda = Expression.Lambda<Func<IObjectFactory, string, object>>(
                    setup,
                    new ParameterExpression[] { factory, json }
                    );

            Console.WriteLine(lambda.ToString());

            return lambda.Compile();
            }
            catch (Exception ex)
            {
                throw new Exception("parameters: " + parameters.Length.ToString() +" :: " + String.Join(", ", parameters.Select(x=> x.Key + " : " + x.Value.Name)), ex);
            }
        }

        Func<IObjectFactory, string, object> _internalFunc;

        internal object Invoke(IObjectFactory objectFactory, string jsonRpc)
        {
            return _internalFunc(objectFactory, jsonRpc);
            //ValueTuple<string> p = new ValueTuple<string>();
            //objectFactory.DeserializeJsonRef(jsonRpc, ref p, ref id, parameters);
            //return dele.Method.Invoke(dele.Target, new[] { p.Item1 });
        }
    }
}
