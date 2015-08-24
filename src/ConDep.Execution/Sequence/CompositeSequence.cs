using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ConDep.Dsl.Config;
using ConDep.Dsl.Logging;
using ConDep.Dsl.Validation;

namespace ConDep.Dsl.Sequence
{
    internal class CompositeSequence : IOfferCompositeSequence
    {
        private readonly string _compositeName;
        internal readonly List<IExecuteRemotely> _sequence = new List<IExecuteRemotely>();

        public CompositeSequence(string compositeName)
        {
            _compositeName = compositeName;
        }

        public void Add(IExecuteRemotely operation, bool addFirst = false)
        {
            if (addFirst)
            {
                _sequence.Insert(0, operation);
            }
            else
            {
                _sequence.Add(operation);
            }
        }

        public virtual void Execute(ServerConfig server, IReportStatus status, ConDepSettings settings, CancellationToken token)
        {
            Logger.WithLogSection(_compositeName, () =>
                {
                    foreach (var element in _sequence)
                    {
                        token.ThrowIfCancellationRequested();

                        IExecuteRemotely elementToExecute = element;
                        if (element is CompositeSequence)
                            elementToExecute.Execute(server, status, settings, token);
                        else
                            Logger.WithLogSection(element.Name, () => elementToExecute.Execute(server, status, settings, token));

                    }
                });
        }

        public IOfferCompositeSequence NewCompositeSequence(RemoteCompositeOperation operation)
        {
            var seq = new CompositeSequence(operation.Name);
            _sequence.Add(seq);
            return seq;
        }

        public IOfferCompositeSequence NewConditionalCompositeSequence(Predicate<ServerInfo> condition)
        {
            var sequence = new CompositeConditionalSequence(Name, condition, true);
            _sequence.Add(sequence);
            return sequence;
        }

        public IOfferCompositeSequence NewConditionalCompositeSequence(string conditionScript)
        {
            var sequence = new CompositeConditionalSequence(Name, conditionScript);
            _sequence.Add(sequence);
            return sequence;
        }

        public void DryRun()
        {
            foreach (var item in _sequence)
            {
                Logger.WithLogSection(item.Name, () => item.DryRun());
            }
        }

        public bool IsValid(Notification notification)
        {
            return _sequence.All(x => x.IsValid(notification));
        }

        public virtual string Name { get { return _compositeName; } }
    }
}