// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Azure.ResourceManager.Samples.Common
{
    public static class Utilities
    {
        public static Action<string> LoggerMethod { get; set; }
        public static Func<string> PauseMethod { get; set; }
        public static string ProjectPath { get; set; }
        private static Random _random => new Random();

        static Utilities()
        {
            LoggerMethod = Console.WriteLine;
            PauseMethod = Console.ReadLine;
            ProjectPath = ".";
        }

        public static void Log(string message)
        {
            LoggerMethod.Invoke(message);
        }

        public static void Log(object obj)
        {
            if (obj != null)
            {
                LoggerMethod.Invoke(obj.ToString());
            }
            else
            {
                LoggerMethod.Invoke("(null)");
            }
        }

        public static void Log()
        {
            Utilities.Log("");
        }

        public static string ReadLine() => PauseMethod.Invoke();

        public static string CreateRandomName(string namePrefix) => $"{namePrefix}{_random.Next(9999)}";

        public static string CreatePassword() => "azure12345QWE!";

        public static string CreateUsername() => "tirekicker";

        public static async Task<List<T>> ToEnumerableAsync<T>(this IAsyncEnumerable<T> asyncEnumerable)
        {
            List<T> list = new List<T>();
            await foreach (T item in asyncEnumerable)
            {
                list.Add(item);
            }
            return list;
        }

        public static async Task<PublicIPAddressResource> CreatePublicIP(ResourceGroupResource resourceGroup, string publicIPName = null)
        {
            publicIPName = publicIPName is null ? CreateRandomName("pip") : publicIPName;
            var publicIPInput = new PublicIPAddressData()
            {
                Location = resourceGroup.Data.Location,
                PublicIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
            };
            var publicIPLro = await resourceGroup.GetPublicIPAddresses().CreateOrUpdateAsync(WaitUntil.Completed, publicIPName, publicIPInput);
            return publicIPLro.Value;
        }
    }
}
