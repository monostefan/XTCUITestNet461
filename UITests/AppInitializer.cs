using System;
using System.IO;
using System.Linq;
using Xamarin.UITest;

namespace XTC.UITestReproduction.UITests
{
    public class AppInitializer
    {
        public static IApp StartApp (Platform platform)
        {
        	return ConfigureApp.Android.StartApp ();
        }
    }
}
