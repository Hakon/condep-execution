﻿using System;
using System.Linq;
using ConDep.Dsl.Config;

namespace ConDep.Dsl.LoadBalancer
{
    internal class LoadBalancerLookup : ILookupLoadBalancer
    {
        private readonly LoadBalancerConfig _loadBalancerSettings;

        public LoadBalancerLookup(LoadBalancerConfig loadBalancerSettings)
        {
            _loadBalancerSettings = loadBalancerSettings;
        }

        public ILoadBalance GetLoadBalancer()
        {
            if (_loadBalancerSettings != null)
            {
                if(!string.IsNullOrWhiteSpace(_loadBalancerSettings.Provider))
                {
                    var assemblyHandler = new ConDepAssemblyHandler(_loadBalancerSettings.Provider);
                    var assembly = assemblyHandler.GetAssembly();

                    var type = assembly.GetTypes().FirstOrDefault(t => typeof(ILoadBalance).IsAssignableFrom(t));
                    var loadBalancer = Activator.CreateInstance(type, _loadBalancerSettings) as ILoadBalance;
                    loadBalancer.Mode = _loadBalancerSettings.GetModeAsEnum();
                    return loadBalancer;
                }
            }
            return new DefaultLoadBalancer();
        }
        
    }
}