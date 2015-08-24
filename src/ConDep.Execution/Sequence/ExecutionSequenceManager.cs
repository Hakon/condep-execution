using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ConDep.Dsl.Config;
using ConDep.Dsl.LoadBalancer;
using ConDep.Dsl.Logging;
using ConDep.Dsl.Validation;

namespace ConDep.Dsl.Sequence
{
    internal interface IManageExecutionSequence
    {
        IOfferLocalSequence NewLocalSequence(string name);
        IOfferRemoteSequence NewRemoteSequence(string name, bool paralell = false);
        void Execute(IReportStatus status, ConDepSettings settings, CancellationToken token);
        bool IsValid(Notification notification);
        void DryRun(ConDepSettings settings);
    }

    internal class ExecutionSequenceManager : IValidate, IManageExecutionSequence
    {
        private readonly IEnumerable<ServerConfig> _servers;
        private readonly ILoadBalance _loadBalancer;
        internal readonly List<IOfferLocalSequence> _localSequences = new List<IOfferLocalSequence>();
        internal readonly List<IOfferRemoteSequence> _remoteSequences = new List<IOfferRemoteSequence>();
        private readonly LoadBalancerExecutorBase _internalLoadBalancer;

        public ExecutionSequenceManager(IEnumerable<ServerConfig> servers, ILoadBalance loadBalancer)
        {
            _servers = servers;
            _loadBalancer = loadBalancer;
            _internalLoadBalancer = GetLoadBalancer();
        }

        public IOfferLocalSequence NewLocalSequence(string name)
        {
            var sequence = new LocalSequence(name, this);
            _localSequences.Add(sequence);
            return sequence;
        }

        public IOfferRemoteSequence NewRemoteSequence(string name, bool paralell = false)
        {
            var sequence = new RemoteSequence(name);
            _remoteSequences.Add(sequence);
            return sequence;
        }

        public void Execute(IReportStatus status, ConDepSettings settings, CancellationToken token)
        {
            ExecuteLocalOperations(status, settings, token);
            ExecuteRemoteOperations(status, settings, token);
        }

        private void ExecuteLocalOperations(IReportStatus status, ConDepSettings settings, CancellationToken token)
        {
            Logger.WithLogSection("Local Operations", () =>
            {
                foreach (var localSequence in _localSequences)
                {
                    token.ThrowIfCancellationRequested();

                    IOfferLocalSequence sequence = localSequence;
                    Logger.WithLogSection(localSequence.Name, () => sequence.Execute(status, settings, token));
                }
            });
        }

        private void ExecuteRemoteOperations(IReportStatus status, ConDepSettings settings, CancellationToken token)
        {
            Logger.WithLogSection("Remote Operations", () =>
            {
                var serversToDeployTo = _internalLoadBalancer.GetServerExecutionOrder(status, settings, token);
                var errorDuringLoadBalancing = false;

                foreach (var server in serversToDeployTo)
                {
                    var serverToDeployTo = server;
                    Logger.WithLogSection(serverToDeployTo.Name, () =>
                    {
                        try
                        {
                            _internalLoadBalancer.BringOffline(serverToDeployTo, status, settings, token);
                            if (!serverToDeployTo.LoadBalancerState.PreventDeployment)
                            {
                                foreach (var remoteSequence in _remoteSequences)
                                {
                                    token.ThrowIfCancellationRequested();

                                    var sequence = remoteSequence;
                                    Logger.WithLogSection(remoteSequence.Name,
                                        () => sequence.Execute(serverToDeployTo, status, settings, token));
                                }
                            }
                        }
                        catch
                        {
                            errorDuringLoadBalancing = true;
                            throw;
                        }
                        finally
                        {
                            if (!errorDuringLoadBalancing && !settings.Options.StopAfterMarkedServer)
                            {
                                _internalLoadBalancer.BringOnline(serverToDeployTo, status, settings, token);
                            }
                        }
                    });
                }
            });
        }

        public bool IsValid(Notification notification)
        {
            return _localSequences.All(x => x.IsValid(notification));
        }

        public void DryRun(ConDepSettings settings)
        {
            Logger.WithLogSection("Local Operations", () =>
            {
                foreach (var item in _localSequences)
                {
                    Logger.WithLogSection(item.Name, () => { item.DryRun(); });
                }
            });

            var loadBalancer = GetDryRunLoadBalancer();
            Logger.WithLogSection("Remote Operations", () =>
            {
                foreach (var server in _servers)
                {
                    Logger.WithLogSection(server.Name, () =>
                    {
                        loadBalancer.BringOffline(server, new StatusReporter(), settings, new CancellationToken());
                        foreach (var item in _remoteSequences)
                        {
                            Logger.WithLogSection(item.Name, () => { item.DryRun(); });
                        }
                        loadBalancer.BringOnline(server, new StatusReporter(), settings, new CancellationToken());
                    });
                }
            });
        }

        private LoadBalancerExecutorBase GetLoadBalancer()
        {
            //if (_paralell)
            //{
            //    return new ParalellRemoteExecutor(_servers);
            //}

            switch (_loadBalancer.Mode)
            {
                case LbMode.Sticky:
                    return new StickyLoadBalancerExecutor(_loadBalancer);
                case LbMode.RoundRobin:
                    return new RoundRobinLoadBalancerExecutor(_servers, _loadBalancer);
                default:
                    throw new ConDepLoadBalancerException(string.Format("Load Balancer mode [{0}] not supported.",
                                                                    _loadBalancer.Mode));
            }
        }

        private LoadBalancerExecutorBase GetDryRunLoadBalancer()
        {
            switch (_loadBalancer.Mode)
            {
                case LbMode.Sticky:
                    return new StickyLoadBalancerExecutor(new DefaultLoadBalancer());
                case LbMode.RoundRobin:
                    return new RoundRobinLoadBalancerExecutor(_servers, new DefaultLoadBalancer());
                default:
                    throw new ConDepLoadBalancerException(string.Format("Load Balancer mode [{0}] not supported.",
                                                                    _loadBalancer.Mode));
            }
        }
    }
}