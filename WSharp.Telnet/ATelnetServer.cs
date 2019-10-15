﻿using WSharp.Extensions;
using WSharp.Logging.Loggers;
using WSharp.Serialization;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace WSharp.Telnet
{
    public abstract class ATelnetServer<T> : ITelnetServer<T>
       where T : ITelnetConnection
    {
        #region FIELDS
        
        private bool _isRunning;
        private bool _isAccepting;
        private bool _isDisposingAllConnections;

        #endregion FIELDS


        #region CONSTRUCTOR
        
        protected ATelnetServer(ITelnetServerSettings settings, ILogger logger, ISerializerDeserializer serializerDeserializer)
        {
            Settings = settings;
            SerializerDeserializer = serializerDeserializer;

            Connections = new ObservableCollection<T>();
            Connections.CollectionChanged += OnClientsCollectionChanged;

            Logger = logger;
            Logger.Log(ClassName, $"Created new telnetServer on {settings.PortNumber}.");
        }

        #endregion CONSTRUCTOR


        #region PROPERTIES

        protected abstract string ClassName { get; }

        protected object Lock { get; } = new object();

        protected ITelnetServerSettings Settings { get; }
        protected ISerializerDeserializer SerializerDeserializer { get; }
        protected CancellationTokenSource AcceptClientCancelTokenSource { get; set; }

        protected ILogger Logger { get; }

        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (Equals(_isRunning, value))
                    return;

                _isRunning = value;

                Logger.Log(ClassName, $"Telnet server state changed to {value}");
                StateChanged?.Invoke(this, value);
            }
        }

        /// <summary>
        /// A collection that holds all active connections.
        /// If a new connection is added, it is automatically listened to.
        /// When a connection is removed, it is disposed.
        /// If one of the connections throws an exception while trying to read or write, it is also automatically removed.
        /// </summary>
        public ObservableCollection<T> Connections { get; }

        #endregion PROPERTIES


        #region METHODS

        /// <summary>
        /// Starts listening to any ip address on the given port number (settings).
        /// If a new listener comes in, it is added to the connections collection.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public virtual Task StartAsync(CancellationToken cancellationToken = default)
            => Task.Factory.StartNew(() =>
            {
                TcpListener listener;
                lock (Lock)
                {
                    if (IsRunning)
                        throw new NotSupportedException("Cannot start listening twice");

                    Connections.RemoveWhere(x => x.IsDisposed);
                    listener = new TcpListener(IPAddress.Any, Settings.PortNumber);
                    listener.Start();
                    IsRunning = true;
                }

                if (AcceptClientCancelTokenSource?.IsCancellationRequested != false)
                    AcceptClientCancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                else
                    AcceptClientCancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, AcceptClientCancelTokenSource?.Token ?? default);

                Logger.Log(ClassName, $"Telnet server started listening on port {Settings.PortNumber}");

                foreach (var connection in Connections)
                    _ = connection.StartListeningAsync(AcceptClientCancelTokenSource.Token);

                while (!AcceptClientCancelTokenSource.IsCancellationRequested)
                {
                    _isAccepting = true;
                    listener.BeginAcceptTcpClient(new AsyncCallback(DoAcceptTcpClientCallback), Tuple.Create(listener, AcceptClientCancelTokenSource.Token));

                    while (!AcceptClientCancelTokenSource.IsCancellationRequested && _isAccepting) { }
                }

                lock (Lock)
                {
                    IsRunning = false;
                    listener.Stop();
                    Logger.Log(ClassName, $"Telnet server stopped listening at port {Settings.PortNumber}");
                }
            });

        protected void DoAcceptTcpClientCallback(IAsyncResult asyncResult)
        {
            lock (Lock)
            {
                if (!IsRunning)
                    return;

                var tuple = (Tuple<TcpListener, CancellationToken>)asyncResult.AsyncState;
                var listener = tuple.Item1;
                var cancellationToken = tuple.Item2;

                var tcpClient = listener.EndAcceptTcpClient(asyncResult);

                var connection = CreateNewConnection(tcpClient);
                Connections.Add(connection);
                Logger.Log(ClassName, $"Added new connection ({connection.RemoteHost})");
                _isAccepting = false;
            }
        }

        public virtual void Stop()
        {
            lock (Lock)
            {
                Logger.Log(ClassName, $"Stopping {GetType().Name}");
                AcceptClientCancelTokenSource.Cancel();
                Logger.Log(ClassName, $"Stopped {GetType().Name}");
            }
        }

        protected virtual void OnClientsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (!IsRunning)
                return;

            var addedConnections = e.NewItems?.Cast<T>();
            var removedConnections = e.OldItems?.Cast<T>();

            if (addedConnections != null)
                foreach (var connection in addedConnections)
                {
                    _ = HandleClientAsync(connection);
                    connection.PropertyChanged += OnConnectionPropertyChanged;
                }

            if (removedConnections != null)
                foreach (var connection in removedConnections)
                {
                    if (!connection.IsDisposed)
                        connection.TryDispose();

                    connection.PropertyChanged -= OnConnectionPropertyChanged;
                    connection.SentAsync -= RaiseSentAsync;
                }
        }

        protected virtual async Task HandleClientAsync(T connection)
        {
            connection.ReceivedAsync += RaiseReceivedAsync;
            connection.SentAsync += RaiseSentAsync;

            try
            {
                await connection.StartListeningAsync(AcceptClientCancelTokenSource.Token);
            }
            finally
            {
            }

            // Connections.RemoveFirst(x => x.Id == connection.Id);
        }

        protected abstract T CreateNewConnection(TcpClient tcpClient);

        protected virtual Task RaiseReceivedAsync(ITelnetConnection connection, SentReceivedEventArgs args)
            => ReceivedAsync?.Invoke(connection, args);
        protected virtual Task RaiseSentAsync(ITelnetConnection connection, SentReceivedEventArgs args)
            => SentAsync?.Invoke(connection, args);

        public virtual Task DisposeConnectionsAsync()
            => Task.Factory.StartNew(() =>
        {
            lock (Lock)
            {
                _isDisposingAllConnections = true;

                foreach (var connection in Connections)
                {
                    try
                    {
                        connection.Dispose();
                    }
                    catch (Exception e)
                    {
                        Logger.Log(ClassName, new object[] { e.GetType().Name, e.Message }, TraceEventType.Error);
                        throw;
                    }
                }

                Connections.Clear();
                _isDisposingAllConnections = false;
            }
        });

        public void Dispose()
        {
            DisposeConnectionsAsync();
        }

        private void OnConnectionPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!(sender is ITelnetConnection connection))
                return;
            if (_isDisposingAllConnections)
                return;

            switch (e.PropertyName)
            {
                case nameof(ITelnetConnection.IsDisposed):
                    Connections.RemoveWhere(x => x.Id == connection.Id);
                    break;
            }
        }

        #endregion METHODS


        #region EVENTS

        public event SentReceivedEventHandler ReceivedAsync;
        public event SentReceivedEventHandler SentAsync;
        public event EventHandler<bool> StateChanged;

        #endregion EVENTS
    }
}
