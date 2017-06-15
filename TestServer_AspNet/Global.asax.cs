using System;

namespace TestServer_AspNet
{
    public class Global : System.Web.HttpApplication
    {
        // Optiion 1: Activate Json RPC service(s) in Global.asax.cs
        //
        //private static HelloWorldService service;

        //public override void Init()
        //{
        //    base.Init();
        //    // alexbclarke: Init() can be called several times.
        //    // This will cause "An item with the same key has already been added." error in
        //    // AustinHarris.JsonRpc.AddService() method
        //    // One way to fix it is to ensure that the Service variable is only instanciated once.
        //    // Another option is to check is the disctionary already contains the method before
        //    // adding it in AustinHarris.JsonRpc.AddService()

        //    if (null == service)
        //    {
        //        service = new HelloWorldService();
        //    }
        //}

        protected void Application_Start(object sender, EventArgs e)
        {
        }
    }
}