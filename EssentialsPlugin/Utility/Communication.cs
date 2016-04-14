﻿namespace EssentialsPlugin.Utility
{
    using System;
    using System.IO;
    using System.Linq;
    using NLog;
    using Sandbox.Common;
    using Sandbox.Common.ObjectBuilders;
    using Sandbox.ModAPI;
    using System.Collections.Generic;
    using SEModAPIExtensions.API;
    using SEModAPIInternal.API.Common;
    using SEModAPIInternal.API.Entity;
    using SEModAPIInternal.API.Entity.Sector.SectorObject;
    using SEModAPIInternal.API.Server;
    using VRage;
    using VRageMath;
    using SEModAPI.API.Definitions;
    using System.Text;
    using Settings;
    using VRage.Game;

    public static class Communication
    {
        private static readonly Logger Log = LogManager.GetLogger( "PluginLog" );
        private static Random m_random = new Random( );

        public static void SendPublicInformation( string infoText )
        {
            if ( infoText == "" )
                return;
           
            ServerMessageItem MessageItem = new ServerMessageItem( );
            MessageItem.From = PluginSettings.Instance.ServerChatName;
            MessageItem.Message = infoText;

            string messageString = MyAPIGateway.Utilities.SerializeToXML( MessageItem );
            byte[ ] data = Encoding.UTF8.GetBytes( messageString );

            if ( ChatManager.EnableData )
            {
                BroadcastDataMessage( DataMessageType.Message, data );
            }
            else
                ChatManager.Instance.SendPublicChatMessage( infoText );

            ChatManager.Instance.AddChatHistory( new ChatManager.ChatEvent( DateTime.Now, 0, infoText ) );
        }

        public static void SendPrivateInformation( ulong playerId, string infoText, string from = null )
        {
            if ( infoText == "" )
                return;

            ServerMessageItem MessageItem = new ServerMessageItem( );

            if ( from == null )
                MessageItem.From = PluginSettings.Instance.ServerChatName;

            else if ( PluginSettings.Instance.WhisperChatPrefix )
                MessageItem.From = "<whisper> " + from;

            else
                MessageItem.From = from;

            MessageItem.Message = infoText;

            string messageString = MyAPIGateway.Utilities.SerializeToXML( MessageItem );
            byte[ ] data = Encoding.UTF8.GetBytes( messageString );

            if ( ChatManager.EnableData )
            {
                SendDataMessage( playerId, DataMessageType.Message, data );
            }
            else
                ChatManager.Instance.SendPrivateChatMessage( playerId, infoText );

            ChatManager.ChatEvent chatItem = new ChatManager.ChatEvent( );
            chatItem.Timestamp = DateTime.Now;
            chatItem.RemoteUserId = (from == null ? 0 : PlayerMap.Instance.GetSteamIdFromPlayerName( from ));
            chatItem.Message = (from == null ? infoText : ( $"{{whisper}} to {PlayerMap.Instance.GetFastPlayerNameFromSteamId( playerId )}: {infoText}" ));
            ChatManager.Instance.AddChatHistory( chatItem );
        }

        //TODO: Kill PlayerMap
        public static void SendFactionClientMessage( ulong playerSteamId, string message )
        {
            ServerMessageItem MessageItem = new ServerMessageItem( );
            if ( PluginSettings.Instance.FactionChatPrefix )
                MessageItem.From = "<faction> " + PlayerMap.Instance.GetFastPlayerNameFromSteamId( playerSteamId );
            else
                MessageItem.From = PlayerMap.Instance.GetFastPlayerNameFromSteamId( playerSteamId );

            MessageItem.Message = message;

            string messageString = MyAPIGateway.Utilities.SerializeToXML( MessageItem );
            byte[ ] data = Encoding.UTF8.GetBytes( messageString );

            foreach ( ulong steamId in PlayerManager.Instance.ConnectedPlayers )
            {
                if ( Player.CheckPlayerSameFaction( playerSteamId, steamId ) )
                {
                    if ( ChatManager.EnableData )
                    {
                        SendDataMessage( steamId, DataMessageType.Message, data );
                        ChatManager.Instance.AddChatHistory( new ChatManager.ChatEvent( DateTime.Now, playerSteamId, "{faction message}: " + message ) );
                    }
                    else
                        ChatManager.Instance.SendPrivateChatMessage( steamId, message );
                }
            }
        }

        public static void Notification( ulong steamId, MyFontEnum color, int timeInMilliseconds, string message )
        {
            ServerNotificationItem messageItem = new ServerNotificationItem
                                                 {
                                                     color = color,
                                                     time = timeInMilliseconds,
                                                     message = message
                                                 };

            string messageString = MyAPIGateway.Utilities.SerializeToXML( messageItem );
            byte[ ] data = Encoding.UTF8.GetBytes( messageString );

            if ( steamId != 0 )
                SendDataMessage( steamId, DataMessageType.Notification, data );
            else
                BroadcastDataMessage( DataMessageType.Notification, data );
        }

        public static void DisplayDialog( ulong steamId, string header, string subheader, string content, string buttonText = "OK" )
        {
            ServerDialogItem messageItem = new ServerDialogItem
                                           {
                                               title = header,
                                               header = subheader,
                                               content = content,
                                               buttonText = buttonText
                                           };

            string messageString = MyAPIGateway.Utilities.SerializeToXML( messageItem );
            byte[ ] data = Encoding.UTF8.GetBytes( messageString );

            SendDataMessage( steamId, DataMessageType.Dialog, data );
        }

        public static void DisplayDialog( ulong steamId, ServerDialogItem messageItem )
        {
            string messageString = MyAPIGateway.Utilities.SerializeToXML( messageItem );
            byte[ ] data = Encoding.UTF8.GetBytes( messageString );

            SendDataMessage( steamId, DataMessageType.Dialog, data );
        }

        public static void MoveMessage( ulong steamId, string moveType, double x, double y, double z, long entityId = 0 )
        {
            ServerMoveItem MoveItem = new ServerMoveItem( );
            MoveItem.moveType = moveType;
            MoveItem.x = x;
            MoveItem.y = y;
            MoveItem.z = z;
            MoveItem.entityId = entityId;

            string messageString = MyAPIGateway.Utilities.SerializeToXML( MoveItem );
            byte[ ] data = Encoding.UTF8.GetBytes( messageString );
            if ( steamId != 0 )
                SendDataMessage( steamId, DataMessageType.Move, data );
            else
                BroadcastDataMessage( DataMessageType.Move, data );
        }

        public static void MoveMessage( ulong steamId, string moveType, Vector3D position )
        {
            ServerMoveItem MoveItem = new ServerMoveItem( );
            MoveItem.moveType = moveType;
            MoveItem.x = position.X;
            MoveItem.y = position.Y;
            MoveItem.z = position.Z;

            string messageString = MyAPIGateway.Utilities.SerializeToXML( MoveItem );
            byte[ ] data = Encoding.UTF8.GetBytes( messageString );

            SendDataMessage( steamId, DataMessageType.Move, data );
        }

        public static void WaypointMessage( WaypointItem item )
        {
            string messageString = MyAPIGateway.Utilities.SerializeToXML( item );
            byte[ ] data = Encoding.UTF8.GetBytes( messageString );
            SendDataMessage( item.SteamId, DataMessageType.Waypoint, data );
        }

        public static void WaypointMessage( ulong userId, string waypointString )
        {
            byte[ ] data = Encoding.UTF8.GetBytes( waypointString );
            SendDataMessage( userId, DataMessageType.Waypoint, data );
        }

        public static void WaypointMessage( ServerWaypointItem serverItem )
        {
            WaypointItem item = new WaypointItem( );
            item.Name = serverItem.Name;
            item.Position = new Vector3D( serverItem.X, serverItem.Y, serverItem.Z );
            //item.Remove = serverItem.Enabled;
            item.SteamId = 0;
            item.Text = serverItem.Name;

            string messageString = MyAPIGateway.Utilities.SerializeToXML( item );
            byte[ ] data = Encoding.UTF8.GetBytes( messageString );
            BroadcastDataMessage( DataMessageType.Waypoint, data );
        }

        public static void SendDataMessage( ulong steamId, DataMessageType messageType, byte[ ] data )
        {
            //this may be unsafe, but whatever, my sanity requires the enum
            long msgId = (long)messageType;

            //TODO: Check for max message size of 4kB
            string msgIdString = msgId.ToString( );
            byte[ ] newData = new byte[data.Length + msgIdString.Length + 1];
            newData[0] = (byte)msgIdString.Length;
            for ( int r = 0; r < msgIdString.Length; r++ )
                newData[r + 1] = (byte)msgIdString[r];

            Buffer.BlockCopy( data, 0, newData, msgIdString.Length + 1, data.Length );

            Wrapper.GameAction( ( ) =>
                                {
                                    MyAPIGateway.Multiplayer.SendMessageTo( 9000, newData, steamId );
                                } );
            //ServerNetworkManager.SendDataMessage( 9000, newData, steamId );
        }

        public static void BroadcastDataMessage( DataMessageType messageType, byte[ ] data )
        {
            //this may be unsafe, but whatever, my sanity requires the enum
            long msgId = (long)messageType;

            string msgIdString = msgId.ToString( );
            byte[ ] newData = new byte[data.Length + msgIdString.Length + 1];
            newData[0] = (byte)msgIdString.Length;
            for ( int r = 0; r < msgIdString.Length; r++ )
                newData[r + 1] = (byte)msgIdString[r];

            Buffer.BlockCopy( data, 0, newData, msgIdString.Length + 1, data.Length );

            Wrapper.GameAction( ( ) =>
                                {
                                    MyAPIGateway.Multiplayer.SendMessageToOthers( 9000, newData );
                                } );
        }

        public class ServerMessageItem
        {
            public string From
            {
                get; set;
            }
            public string Message
            {
                get; set;
            }
        }

        public class ServerDialogItem
        {
            public string title
            {
                get; set;
            }
            public string header
            {
                get; set;
            }
            public string content
            {
                get; set;
            }
            public string buttonText
            {
                get; set;
            }
        }

        public class ServerNotificationItem
        {
            public MyFontEnum color
            {
                get; set;
            }
            public int time
            {
                get; set;
            }
            public string message
            {
                get; set;
            }
        }

        public class ServerMoveItem
        {
            public string moveType
            {
                get; set;
            }
            public double x
            {
                get; set;
            }
            public double y
            {
                get; set;
            }
            public double z
            {
                get; set;
            }
            public long entityId
            {
                get; set;
            }
        }

        public enum DataMessageType
        {
            Test = 5000,
            VoxelHeader,
            VoxelPart,
            Message,
            RemoveStubs,
            ChangeServer,
            ServerSpeed,
            Credits,

            //skipped a few addresses to avoid conflict
            //just in case
            Dialog = 5020,
            Move,
            Notification,
            MaxSpeed,
            ServerInfo,
            Waypoint
        }
    }
}
