using System;
using System.Collections.Generic;
using JointFires.Sensors;
using Mirage;
using NuclearOption.Networking;
using UnityEngine;

namespace JointFires.Net
{
    // Owns the link between the mod and Mirage (same design as Multi-Crew's CrewNet): finds the
    // network manager, registers our handlers each time the game creates a new server or client
    // MessageHandler (it makes a fresh one on every start), and runs the hello handshake. Nothing
    // but JfHelloMsg is ever sent to a peer before it has proven it runs the mod, because unknown
    // messages cost the receiving peer an error penalty (MessageHandler.InvokeHandler -> SetError(60)).
    internal static class JfNet
    {
        // Bump Protocol on any wire change. JfHelloMsg / JfHelloAckMsg layouts must never change.
        public const byte Protocol = 1;
        public const ushort Version = 1;

        private static NetworkManagerNuclearOption manager;
        private static float nextManagerSearch;

        private static MessageHandler serverHandler;
        private static MessageHandler clientHandler;
        private static NetworkServer disconnectHookedServer;
        private static bool helloSent;

        // Server side: peers that sent a hello with our protocol.
        private static readonly HashSet<INetworkPlayer> modded = new HashSet<INetworkPlayer>();

        public static NetworkServer Server => serverHandler != null ? manager.Server : null;
        public static NetworkClient Client => clientHandler != null ? manager.Client : null;

        // Client side: the server confirmed it runs a matching version of the mod.
        public static bool ServerHasMod { get; private set; }

        public static bool IsModded(INetworkPlayer player) => player != null && modded.Contains(player);

        public static void Tick()
        {
            if (!FindManager())
            {
                return;
            }

            NetworkServer server = manager.Server;
            if (server != null && server.Active && server.MessageHandler != null)
            {
                if (!ReferenceEquals(server.MessageHandler, serverHandler))
                {
                    RegisterServer(server);
                }
                SensorServer.Tick();
            }
            else if (serverHandler != null)
            {
                ResetServer();
                Plugin.Log.LogInfo("[Net] Server stopped; state cleared");
            }

            NetworkClient client = manager.Client;
            if (client != null && client.Active && client.MessageHandler != null)
            {
                if (!ReferenceEquals(client.MessageHandler, clientHandler))
                {
                    RegisterClient(client);
                }
                TrySendHello(client);
            }
            else if (clientHandler != null)
            {
                ResetClient();
                Plugin.Log.LogInfo("[Net] Client stopped; state cleared");
            }
        }

        public static void ResetAll()
        {
            ResetServer();
            ResetClient();
        }

        private static void ResetServer()
        {
            serverHandler = null;
            modded.Clear();
            SensorServer.Reset();
        }

        private static void ResetClient()
        {
            clientHandler = null;
            helloSent = false;
            ServerHasMod = false;
            SensorClient.Reset();
        }

        // ---- client -> server ----

        public static void SendToServer<T>(T msg)
        {
            // Only after the server proved it has the mod; hello goes through TrySendHello.
            if (!ServerHasMod || clientHandler == null)
            {
                return;
            }
            try
            {
                manager.Client.Send(msg);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Net] Send {typeof(T).Name} failed: {e.Message}");
            }
        }

        // ---- server -> client ----

        // Only to peers that proved they run the mod.
        public static void SendToPlayer<T>(INetworkPlayer player, T msg)
        {
            if (!IsModded(player))
            {
                return;
            }
            try
            {
                player.Send(msg);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Net] Send {typeof(T).Name} to {player} failed: {e.Message}");
            }
        }

        // ---- handshake ----

        private static void OnHello(INetworkPlayer player, JfHelloMsg msg)
        {
            // Always ack: the ack layout is frozen, so a mismatched client can still read it and warn.
            try
            {
                player.Send(new JfHelloAckMsg { Protocol = Protocol, Version = Version });
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Net] Hello ack to {player} failed: {e.Message}");
            }
            if (msg.Protocol != Protocol)
            {
                Plugin.Log.LogWarning($"[Server] {player} runs protocol {msg.Protocol}, server runs {Protocol}; Joint Fires disabled for them");
                return;
            }
            modded.Add(player);
            Plugin.Log.LogInfo($"[Server] {player} has Joint Fires (v{msg.Version})");
        }

        private static void OnHelloAck(INetworkPlayer _, JfHelloAckMsg msg)
        {
            if (msg.Protocol != Protocol)
            {
                Plugin.Log.LogWarning($"[Client] Server runs Joint Fires protocol {msg.Protocol}, we run {Protocol}; Joint Fires disabled");
                return;
            }
            ServerHasMod = true;
            Plugin.Log.LogInfo($"[Client] Server has Joint Fires (v{msg.Version})");
        }

        // ---- plumbing ----

        private static bool FindManager()
        {
            if (manager != null)
            {
                return true;
            }
            if (Time.unscaledTime < nextManagerSearch)
            {
                return false;
            }
            nextManagerSearch = Time.unscaledTime + 2f;
            manager = UnityEngine.Object.FindObjectOfType<NetworkManagerNuclearOption>();
            return manager != null;
        }

        private static void RegisterServer(NetworkServer server)
        {
            JfSerializers.Register();
            ResetServer();
            serverHandler = server.MessageHandler;

            serverHandler.RegisterHandler<JfHelloMsg>(Guard<JfHelloMsg>(OnHello), false);
            serverHandler.RegisterHandler<SensorQueryMsg>(Guard<SensorQueryMsg>(SensorServer.OnQuery), false);

            // The server object lives across sessions, so hook its disconnect event only once.
            if (!ReferenceEquals(disconnectHookedServer, server))
            {
                disconnectHookedServer = server;
                server.Disconnected.AddListener(OnServerDisconnected);
            }
            Plugin.Log.LogInfo("[Net] Server handlers registered");
        }

        private static void RegisterClient(NetworkClient client)
        {
            JfSerializers.Register();
            ResetClient();
            clientHandler = client.MessageHandler;

            clientHandler.RegisterHandler<JfHelloAckMsg>(Guard<JfHelloAckMsg>(OnHelloAck), false);
            clientHandler.RegisterHandler<SensorReportMsg>(Guard<SensorReportMsg>(SensorClient.OnReport), false);
            Plugin.Log.LogInfo("[Net] Client handlers registered");
        }

        private static void TrySendHello(NetworkClient client)
        {
            // Having a local Player object means we're authenticated and in a mission.
            if (helloSent || !GameManager.GetLocalPlayer<Player>(out _))
            {
                return;
            }
            helloSent = true;
            try
            {
                client.Send(new JfHelloMsg { Protocol = Protocol, Version = Version });
                Plugin.Log.LogInfo("[Net] Sent hello to server");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Net] Hello failed: {e.Message}");
            }
        }

        private static void OnServerDisconnected(INetworkPlayer player)
        {
            if (serverHandler != null)
            {
                modded.Remove(player);
                SensorServer.Forget(player);
            }
        }

        // Exceptions escaping a handler make Mirage charge the sender error points (and the
        // server rate-limits errors), so every handler is wrapped.
        private static MessageDelegateWithPlayer<T> Guard<T>(Action<INetworkPlayer, T> handler)
        {
            return (player, msg) =>
            {
                try
                {
                    handler(player, msg);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"[Net] {typeof(T).Name} handler failed: {e}");
                }
            };
        }
    }
}
