// Copyright 2007-2018 Chris Patterson, Dru Sellers, Travis Smith, et. al.
//
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software distributed
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR
// CONDITIONS OF ANY KIND, either express or implied. See the License for the
// specific language governing permissions and limitations under the License.
namespace MassTransit.AzureServiceBusTransport.Pipeline
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Contexts;
    using GreenPipes;
    using GreenPipes.Agents;
    using Logging;
    using Microsoft.ServiceBus;
    using Microsoft.ServiceBus.Messaging;


    public class MessagingFactoryContextFactory :
        IPipeContextFactory<MessagingFactoryContext>
    {
        static readonly ILog _log = Logger.Get<MessagingFactoryContextFactory>();
        readonly MessagingFactorySettings _messagingFactorySettings;
        readonly RetryPolicy _retryPolicy;
        readonly Uri _serviceUri;

        public MessagingFactoryContextFactory(Uri serviceUri, MessagingFactorySettings messagingFactorySettings, RetryPolicy retryPolicy)
        {
            _serviceUri = new UriBuilder(serviceUri) {Path = ""}.Uri;

            _messagingFactorySettings = messagingFactorySettings;
            _retryPolicy = retryPolicy;
        }

        IPipeContextAgent<MessagingFactoryContext> IPipeContextFactory<MessagingFactoryContext>.CreateContext(ISupervisor supervisor)
        {
            var context = Task.Factory.StartNew(() => CreateConnection(supervisor), supervisor.Stopping, TaskCreationOptions.None, TaskScheduler.Default)
                .Unwrap();

            IPipeContextAgent<MessagingFactoryContext> contextHandle = supervisor.AddContext(context);

            return contextHandle;
        }

        IActivePipeContextAgent<MessagingFactoryContext> IPipeContextFactory<MessagingFactoryContext>.CreateActiveContext(ISupervisor supervisor,
            PipeContextHandle<MessagingFactoryContext> context, CancellationToken cancellationToken)
        {
            return supervisor.AddActiveContext(context, CreateSharedConnection(context.Context, cancellationToken));
        }

        async Task<MessagingFactoryContext> CreateSharedConnection(Task<MessagingFactoryContext> context, CancellationToken cancellationToken)
        {
            var connectionContext = await context.ConfigureAwait(false);

            var sharedConnection = new SharedMessagingFactoryContext(connectionContext, cancellationToken);

            return sharedConnection;
        }

        async Task<MessagingFactoryContext> CreateConnection(ISupervisor supervisor)
        {
            try
            {
                if (supervisor.Stopping.IsCancellationRequested)
                    throw new OperationCanceledException($"The connection is stopping and cannot be used: {_serviceUri}");

                if (_log.IsDebugEnabled)
                    _log.DebugFormat("Connecting: {0}", _serviceUri);

                var messagingFactory = await MessagingFactory.CreateAsync(_serviceUri, _messagingFactorySettings).ConfigureAwait(false);

                messagingFactory.RetryPolicy = _retryPolicy;

                if (_log.IsDebugEnabled)
                    _log.DebugFormat("Connected: {0}", _serviceUri);

                var messagingFactoryContext = new ServiceBusMessagingFactoryContext(messagingFactory, supervisor.Stopped);

                return messagingFactoryContext;
            }
            catch (Exception ex)
            {
                if (_log.IsDebugEnabled)
                    _log.Debug($"MessagingFactory Create Failed: {_serviceUri}", ex);

                throw;
            }
        }
    }
}