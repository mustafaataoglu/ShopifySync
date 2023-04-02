using ShopifySyncEngine;
using System;
using System.Threading.Tasks;

namespace ShopifyProductSync
{
    class Program
    {

        public static void Main(string[] args)
        {
            SyncEngine.Start();
            Console.ReadLine();
        }
    }
}
