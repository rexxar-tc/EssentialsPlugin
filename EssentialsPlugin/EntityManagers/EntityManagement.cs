﻿namespace EssentialsPlugin.EntityManagers
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using EssentialsPlugin.ProcessHandlers;
    using EssentialsPlugin.Utility;
    using Sandbox.Common;
    using Sandbox.Common.ObjectBuilders;
    using Sandbox.ModAPI;
    using Sandbox.ModAPI.Ingame;
    using SEModAPIInternal.API.Common;
    using SEModAPIInternal.API.Entity;
    using VRage.ModAPI;
    using VRage.ObjectBuilders;
    using VRageMath;
    using IMyFunctionalBlock = Sandbox.ModAPI.Ingame.IMyFunctionalBlock;
    using IMyProductionBlock = Sandbox.ModAPI.Ingame.IMyProductionBlock;
    using IMyTerminalBlock = Sandbox.ModAPI.Ingame.IMyTerminalBlock;
    using Sandbox.Engine.Multiplayer;
    using Sandbox.Game.Replication;
    using Sandbox.Game.Entities;
    using Sandbox.Game.Multiplayer;
    using Sandbox.Game.World;
    using VRage.Collections;
    using Sandbox.Game.Entities.Blocks;
    using Sandbox.Game.Entities.Character;
    using SpaceEngineers.Game.ModAPI.Ingame;
    using VRage.Game;
    using VRage.Game.ModAPI;

    class ConcealItem
    {
        public ConcealItem( IMyEntity _entity, string _reason )
        {
            this.entity = _entity;
            this.reason = _reason;
        }
        public ConcealItem( KeyValuePair<IMyEntity, string> kvp )
        {
            this.entity = kvp.Key;
            this.reason = kvp.Value;
        }

        public IMyEntity entity;
        public string reason;
    }

    public class EntityManagement
    {
        private static volatile bool _checkReveal;
        private static volatile bool _checkConceal;
        private static readonly List<long> RemovedGrids = new List<long>( );
        private static readonly List<ulong> Online = new List<ulong>( );
        //private static Queue<ConcealItem> ConcealQueue = new Queue<ConcealItem>( );
        //private static Queue<ConcealItem> MedbayQueue = new Queue<ConcealItem>( );
        private static SortedList<long, ConcealItem> ConcealQueue = new SortedList<long, ConcealItem>( );
        private static SortedList<long, ConcealItem> RevealQueue = new SortedList<long, ConcealItem>( );
        private static SortedList<long, ConcealItem> MedbayQueue = new SortedList<long, ConcealItem>( );
                
        public static void ProcessConcealment( bool force = false )
        {
            //let's not enter the game thread if we don't have to. The poor thing is taxed enough as it is
            ProcessConceal.ForceReveal = RevealQueue.Any( );
            if ( !MedbayQueue.Any( ) && !RevealQueue.Any( ) && !ConcealQueue.Any( ) )
                return;
            //if we are sent the force flag but there's nothing in reveal queue, set the flag back to false
            string forceString = (force ? "force reveal" : "concealment");
            //just so our debug messages can be pretty

            Wrapper.GameAction( ( ) =>
            {
                //we want medbay reveal to have highest priority, even above force reveal
                if ( MedbayQueue.Any( ) )
                {
                    if ( PluginSettings.Instance.DynamicShowMessages )
                        Essentials.Log.Info( "Processing concealment: {0} medbays in reveal queue.", MedbayQueue.Count );

                    ConcealItem MedbayToProcess = MedbayQueue.First( ).Value;
                    MedbayQueue.RemoveAt( 0 );

                    RevealEntity( new KeyValuePair<IMyEntity, string>( MedbayToProcess.entity, MedbayToProcess.reason ) );
                    return;
                }

                if ( RevealQueue.Any( ) )
                {
                    if ( PluginSettings.Instance.DynamicShowMessages )
                        Essentials.Log.Info( "Processing {0}: {1} grids in reveal queue.", forceString, RevealQueue.Count );

                    ConcealItem EntityToProcess = RevealQueue.First( ).Value;
                    RevealQueue.RemoveAt( 0 );

                    RevealEntity( new KeyValuePair<IMyEntity, string>( EntityToProcess.entity, EntityToProcess.reason ) );
                    return;
                }
                //conceal gets bottom priority, it's more important that players have their grids
                if ( ConcealQueue.Any( ) )
                {
                    if ( PluginSettings.Instance.DynamicShowMessages )
                        Essentials.Log.Info( "Processing concealment: {0} grids in concealment queue.", ConcealQueue.Count );

                    ConcealItem EntityToProcess = ConcealQueue.First( ).Value;
                    ConcealQueue.RemoveAt( 0 );

                    ConcealEntity( EntityToProcess.entity );
                    return;
                }

            } );
            return;
        }


        public static void CheckAndConcealEntities( )
		{
			if ( _checkConceal )
				return;

			_checkConceal = true;
			try
			{
				DateTime start = DateTime.Now;
				double distCheck = 0d;
				double blockRules = 0d;
				double getGrids = 0d;
				double co = 0f;

				List<IMyPlayer> players = new List<IMyPlayer>( );
				HashSet<IMyEntity> entities = new HashSet<IMyEntity>( );
				HashSet<IMyEntity> entitiesFiltered = new HashSet<IMyEntity>( );
				HashSet<IMyEntity> entitiesFound = new HashSet<IMyEntity>( );

				try
				{
					MyAPIGateway.Players.GetPlayers( players );
				}
				catch ( Exception ex )
				{
					Essentials.Log.Error( ex, "Error getting players list.  Check and Conceal failed: {0}");
					return;
				}

				try
				{
                    Wrapper.GameAction( ( ) =>
                     {
                         MyAPIGateway.Entities.GetEntities( entities );
                     } );
				}
				catch ( Exception ex )
				{
					Essentials.Log.Error( ex, "Error getting entity list, skipping check" );
					return;
				}

				foreach ( IMyEntity entity in entities )
				{
					if ( !( entity is IMyCubeGrid ) )
						continue;

					if ( !entity.InScene )
						continue;

					entitiesFiltered.Add( entity );
				}

				DateTime getGridsStart = DateTime.Now;
				CubeGrids.GetGridsUnconnected( entitiesFound, entitiesFiltered );
				getGrids += ( DateTime.Now - getGridsStart ).TotalMilliseconds;

				HashSet<IMyEntity> entitiesToConceal = new HashSet<IMyEntity>( );
				foreach ( IMyEntity entity in entitiesFound )
				{
					if ( !( entity is IMyCubeGrid ) )
						continue;

					if ( entity.Physics == null ) // Projection
						continue;

					if ( !entity.InScene )
						continue;

					if ( ( (IMyCubeGrid)entity ).GridSizeEnum != MyCubeSize.Small && !PluginSettings.Instance.ConcealIncludeLargeGrids )
						continue;

					IMyCubeGrid grid = (IMyCubeGrid)entity;

					bool found = false;
					DateTime distStart = DateTime.Now;
					foreach ( IMyPlayer player in players )
					{
						double distance;
						if ( Entity.GetDistanceBetweenGridAndPlayer( grid, player, out distance ) )
						{
							if ( distance < PluginSettings.Instance.DynamicConcealDistance )
							{
								found = true;
							}
						}
					}
					distCheck += ( DateTime.Now - distStart ).TotalMilliseconds;

					if ( !found )
					{
						// Check to see if grid is close to dock / shipyard
						foreach ( IMyCubeGrid checkGrid in ProcessDockingZone.ZoneCache )
						{
							try
							{
								if ( Vector3D.Distance( checkGrid.GetPosition( ), grid.GetPosition( ) ) < 100d )
								{
									found = true;
									break;
								}
							}
							catch
							{
								continue;
							}
						}
					}

					if ( !found )
					{
						// Check for block type rules
						DateTime blockStart = DateTime.Now;
						if ( CheckConcealBlockRules( grid, players ) )
						{
							found = true;
						}

						blockRules += ( DateTime.Now - blockStart ).TotalMilliseconds;
					}

					if ( !found )
					{
						entitiesToConceal.Add( entity );
                    }
				}

				DateTime coStart = DateTime.Now;
				if ( entitiesToConceal.Count > 0 )
					ConcealEntities( entitiesToConceal );
				co += ( DateTime.Now - coStart ).TotalMilliseconds;

				if ( ( DateTime.Now - start ).TotalMilliseconds > 2000 && PluginSettings.Instance.DynamicShowMessages )
					Essentials.Log.Info( "Completed Conceal Check: {0}ms (gg: {3}, dc: {2} ms, br: {1}ms, co: {4}ms)", ( DateTime.Now - start ).TotalMilliseconds, blockRules, distCheck, getGrids, co );

			}
			catch ( Exception ex )
			{
				Essentials.Log.Error( ex );
			}
			finally
			{
				_checkConceal = false;
			}
		}

		private static bool CheckConcealBlockRules( IMyCubeGrid grid, List<IMyPlayer> players )
		{
			List<IMySlimBlock> blocks = new List<IMySlimBlock>( );            

			// Live dangerously
			grid.GetBlocks( blocks, x => x.FatBlock != null );
			//CubeGrids.GetAllConnectedBlocks(_processedGrids, grid, blocks, x => x.FatBlock != null);

			int beaconCount = 0;
			//bool found = false;
			//bool powered = false;
			foreach ( IMySlimBlock block in blocks )
			{
				IMyCubeBlock cubeBlock = block.FatBlock;

				if ( cubeBlock.BlockDefinition.TypeId == typeof( MyObjectBuilder_Beacon ) )
				{
					IMyBeacon beacon = (IMyBeacon)cubeBlock;
					//MyObjectBuilder_Beacon beacon = (MyObjectBuilder_Beacon)cubeBlock.GetObjectBuilderCubeBlock();
					beaconCount++;
					// Keep this return here, as 4 beacons always means true
					if ( beaconCount >= 4 )
					{
						return true;
					}

					if ( !beacon.Enabled )
						continue;

					IMyTerminalBlock terminalBlock = (IMyTerminalBlock)cubeBlock;
					//					Console.WriteLine("Found: {0} {1} {2}", beacon.BroadcastRadius, terminalBlock.IsWorking, terminalBlock.IsFunctional);
					//if (!terminalBlock.IsWorking)
					//{
					//						continue;
					//}

					foreach ( IMyPlayer player in players )
					{
						double distance;
						if ( Entity.GetDistanceBetweenPointAndPlayer( grid.GetPosition( ), player, out distance ) )
						{
							if ( distance < beacon.Radius )
							{
								//								Console.WriteLine("Not concealed due to broadcast radius");
								//found = true;
								//break;
								return true;
							}
						}
					}
				}

				if ( cubeBlock.BlockDefinition.TypeId == typeof( MyObjectBuilder_RadioAntenna ) )
				{
					//MyObjectBuilder_RadioAntenna antenna = (MyObjectBuilder_RadioAntenna)cubeBlock.GetObjectBuilderCubeBlock();
					IMyRadioAntenna antenna = (IMyRadioAntenna)cubeBlock;

					if ( !antenna.Enabled )
						continue;

					IMyTerminalBlock terminalBlock = (IMyTerminalBlock)cubeBlock;
					//if (!terminalBlock.IsWorking)
					//	continue;

					foreach ( IMyPlayer player in players )
					{
						double distance;
						if ( Entity.GetDistanceBetweenPointAndPlayer( grid.GetPosition( ), player, out distance ) )
						{
							if ( distance < antenna.Radius )
							{
								//								Console.WriteLine("Not concealed due to antenna broadcast radius");
								//found = true;
								//break;
								return true;
							}
						}
					}
				}

				if ( cubeBlock.BlockDefinition.TypeId == typeof( MyObjectBuilder_MedicalRoom ) )
				{
					//MyObjectBuilder_MedicalRoom medical = (MyObjectBuilder_MedicalRoom)cubeBlock.GetObjectBuilderCubeBlock();
					IMyMedicalRoom medical = (IMyMedicalRoom)cubeBlock;

					if ( !medical.Enabled )
						continue;

					IMyFunctionalBlock functionalBlock = (IMyFunctionalBlock)cubeBlock;
					//if (!terminalBlock.IsWorking)
					//	continue;

					if ( PluginSettings.Instance.DynamicConcealIncludeMedBays )
					{
						lock ( Online )
						{
							foreach ( ulong connectedPlayer in Online )
							{
								//if (PlayerMap.Instance.GetPlayerIdsFromSteamId(connectedPlayer).Count < 1)
								//continue;

								//long playerId = PlayerMap.Instance.GetPlayerIdsFromSteamId(connectedPlayer).First();
								long playerId = PlayerMap.Instance.GetFastPlayerIdFromSteamId( connectedPlayer );

								if ( functionalBlock.OwnerId == playerId || ( functionalBlock.GetUserRelationToOwner( playerId ) == MyRelationsBetweenPlayerAndBlock.FactionShare ) )
								//if (functionalBlock.Owner == playerId || (functionalBlock.ShareMode == MyOwnershipShareModeEnum.Faction && Player.CheckPlayerSameFaction(functionalBlock.Owner, playerId)))
								//if (medical.HasPlayerAccess(playerId))
								{
									return true;
								}
							}
						}

						/*
						foreach (ulong connectedPlayer in PlayerManager.Instance.ConnectedPlayers)
						{
							//if (PlayerMap.Instance.GetPlayerIdsFromSteamId(connectedPlayer).Count < 1)
								//continue;

							//long playerId = PlayerMap.Instance.GetPlayerIdsFromSteamId(connectedPlayer).First();
							long playerId = PlayerMap.Instance.GetFastPlayerIdFromSteamId(connectedPlayer);
							//if (medical.Owner == playerId || (medical.ShareMode == MyOwnershipShareModeEnum.Faction && Player.CheckPlayerSameFaction(medical.Owner, playerId)))
							if(medical.HasPlayerAccess(playerId))
							{
								return true;
							}
						}
						 */
					}
					else
					{
						return true;
					}
				}

                if ( cubeBlock.BlockDefinition.TypeId == typeof( MyObjectBuilder_CryoChamber ) )
                {
                    MyCryoChamber cryo = (MyCryoChamber)cubeBlock;
                    
                    if ( cryo.Pilot != null )
                        return true;
                }            

                if ( cubeBlock.BlockDefinition.TypeId == typeof( MyObjectBuilder_Refinery ) || cubeBlock.BlockDefinition.TypeId == typeof( MyObjectBuilder_Assembler ) )
				{
					//MyObjectBuilder_ProductionBlock production = (MyObjectBuilder_ProductionBlock)cubeBlock.GetObjectBuilderCubeBlock();
					IMyProductionBlock production = (IMyProductionBlock)cubeBlock;
					if ( !production.Enabled )
						continue;

					if ( production.IsProducing )
						return true;
				}

				foreach ( string subType in PluginSettings.Instance.DynamicConcealIgnoreSubTypeList )
				{
					if ( cubeBlock.BlockDefinition.SubtypeName.Contains( subType ) )
					{
						//						Console.WriteLine("Not concealed due subtype");
						//found = true;
						return true;
					}
				}
			}

			return false;
		}

		private static bool CheckConcealForce( IMyCubeGrid grid, ulong steamId )
		{
			List<IMySlimBlock> blocks = new List<IMySlimBlock>( );

			// Live dangerously
			grid.GetBlocks( blocks, x => x.FatBlock != null );
			foreach ( IMySlimBlock block in blocks )
			{
				IMyCubeBlock cubeBlock = block.FatBlock;

				if ( cubeBlock.BlockDefinition.TypeId == typeof( MyObjectBuilder_MedicalRoom ) )
				{
					MyObjectBuilder_MedicalRoom medical = (MyObjectBuilder_MedicalRoom)cubeBlock.GetObjectBuilderCubeBlock( );

					if ( !medical.Enabled )
						continue;

					IMyTerminalBlock terminalBlock = (IMyTerminalBlock)cubeBlock;
					long playerId = PlayerMap.Instance.GetFastPlayerIdFromSteamId( steamId );
					if ( medical.Owner == playerId || ( medical.ShareMode == MyOwnershipShareModeEnum.Faction && Player.CheckPlayerSameFaction( medical.Owner, playerId ) ) )
					{
						return true;
					}
				}
			}

			return false;
		}

		private static void ConcealEntities( HashSet<IMyEntity> entitesToConceal )
		{
            int ConcealCount = 0;
            foreach ( IMyEntity entity in entitesToConceal )
            {
                long itemId = entity.EntityId;

                if ( MedbayQueue.ContainsKey( itemId ) || RevealQueue.ContainsKey( itemId ) )
                    continue;
                //If this entity is already queued, continue so we don't get dupes or interfere with reveal

                
                ++ConcealCount;
            }

            ConcealQueue.Clear( );
            foreach ( IMyEntity entity in entitesToConceal )
                ConcealQueue.Add( entity.EntityId, new ConcealItem( entity, "" ) );

            if ( PluginSettings.Instance.DynamicShowMessages )
                Essentials.Log.Info( "Queued {0} entities for conceal.", ConcealCount );
        }

		private static void ConcealEntity( IMyEntity entity )
		{
			int pos = 0;
            //RemovedGrids.Clear( );
            try
			{
				if ( !entity.InScene )
					return;

				MyObjectBuilder_CubeGrid builder = CubeGrids.SafeGetObjectBuilder( (IMyCubeGrid)entity );
				if ( builder == null )
					return;
                
                pos = 1;
                IMyCubeGrid grid = (IMyCubeGrid)entity;
				long ownerId;
				string ownerName = string.Empty;
                if ( CubeGrids.GetOwner( builder, out ownerId ) )
                {
                    //ownerId = grid.BigOwners.First();
                    ownerName = PlayerMap.Instance.GetPlayerItemFromPlayerId( ownerId ).Name;

                    if ( !PluginSettings.Instance.DynamicConcealPirates )
                    {
                        if ( ownerName.Contains( "Space Pirate" ) )
                        {
                            if ( PluginSettings.Instance.DynamicShowMessages )
                                Essentials.Log.Info( "Not concealing pirate owned grid {0} -> {1}.", grid.EntityId, grid.DisplayName );
                            return;
                        }
                    }
                }
                Essentials.Log.Info( grid.EntityId.ToString() + " " + ownerId.ToString() + " " + ownerName );

				pos = 2;
                if ( entity.Physics != null )
				{
					entity.Physics.LinearVelocity = Vector3.Zero;
					entity.Physics.AngularVelocity = Vector3.Zero;
				}

				/*
				entity.InScene = false;
				entity.CastShadows = false;
				entity.Visible = false;
				*/

				builder.PersistentFlags = MyPersistentEntityFlags2.None;
				MyAPIGateway.Entities.RemapObjectBuilder( builder );

				pos = 3;
                if ( RemovedGrids.Contains( entity.EntityId ) )
				{
					Essentials.Log.Info( "Concealing - Id: {0} DUPE FOUND - Display: {1} OwnerId: {2} OwnerName: {3}", entity.EntityId, entity.DisplayName, ownerId, ownerName );
                    BaseEntityNetworkManager.BroadcastRemoveEntity( entity, false );
                    MyMultiplayer.ReplicateImmediatelly( MyExternalReplicable.FindByObject( entity ) );
                pos = 4;
                }
				else
				{
					if ( !PluginSettings.Instance.DynamicConcealServerOnly )
					{
                        /*
						if (PluginSettings.Instance.DynamicBlockManagementEnabled)
						{
							bool enable = false;
							lock (BlockManagement.Instance.GridDisabled)
							{
								if(BlockManagement.Instance.GridDisabled.Contains(entity.EntityId))
									enable = true;
							}

							if(enable)
								BlockManagement.Instance.EnableGrid((IMyCubeGrid)entity);
						}
						*/
                        pos = 5;
                        IMyEntity newEntity = MyEntities.CreateFromObjectBuilder( builder );

                        if ( newEntity == null )
                        {
                            Essentials.Log.Warn( "CreateFromObjectBuilder failed: {0}", builder.EntityId );
                            return;
                        }

                        pos = 6;
                        RemovedGrids.Add( entity.EntityId );
                        entity.InScene = false;
                        entity.OnRemovedFromScene( entity );
						BaseEntityNetworkManager.BroadcastRemoveEntity( entity, false );
						MyAPIGateway.Entities.AddEntity( newEntity, false );
                        MyMultiplayer.ReplicateImmediatelly( MyExternalReplicable.FindByObject( newEntity ) );

                        pos = 7;
                        if ( PluginSettings.Instance.DynamicShowMessages )
							Essentials.Log.Info( "Concealed - Id: {0} -> {4} Display: {1} OwnerId: {2} OwnerName: {3}", entity.EntityId, entity.DisplayName, ownerId, ownerName, newEntity.EntityId );
					}
					else
					{
						entity.InScene = false;
						if ( PluginSettings.Instance.DynamicShowMessages )
							Essentials.Log.Info( "Concealed - Id: {0} -> {4} Display: {1} OwnerId: {2} OwnerName: {3}", entity.EntityId, entity.DisplayName, ownerId, ownerName, builder.EntityId );
					}
				}
			}
			catch ( Exception ex )
			{
				Essentials.Log.Error( "Failure while concealing entity {0}.", pos, ex );
			}
		}

		public static void CheckAndRevealEntities( )
		{
            if ( ProcessConceal.ForceReveal )
                RevealQueue.Clear( );
            //force reveal queue can take a long time. let's refresh it in case some grids were removed since first scan

			if ( _checkReveal )
				return;

			_checkReveal = true;
			try
			{
				DateTime start = DateTime.Now;
				double br = 0f;
				double re = 0f;

				List<IMyPlayer> players = new List<IMyPlayer>( );
				HashSet<IMyEntity> entities = new HashSet<IMyEntity>( );
				MyAPIGateway.Players.GetPlayers( players );
                Wrapper.GameAction( ( ) =>
                 {
                     MyAPIGateway.Entities.GetEntities( entities );
                 } );

                Dictionary<IMyEntity, string> entitiesToReveal = new Dictionary<IMyEntity, string>( );
				//HashSet<IMyEntity> entitiesToReveal = new HashSet<IMyEntity>();
				foreach ( IMyEntity entity in entities )
				{
					if ( entity.MarkedForClose )
						continue;

					if ( !( entity is IMyCubeGrid ) )
						continue;

					if ( entity.InScene )
						continue;

					IMyCubeGrid grid = (IMyCubeGrid)entity;
					bool found = false;
					string currentReason = string.Empty;
					foreach ( IMyPlayer player in players )
					{
						double distance = 0f;
						if ( Entity.GetDistanceBetweenGridAndPlayer( grid, player, out distance ) )
						{
							if ( distance < PluginSettings.Instance.DynamicConcealDistance )
							{
								found = true;
								currentReason = string.Format( "{0} distance to grid: {1}", player.DisplayName, distance );
							}
						}
					}

					if ( !found )
					{
						DateTime brStart = DateTime.Now;
						if ( CheckRevealBlockRules( grid, players, out currentReason ) )
						{
							found = true;
						}
						br += ( DateTime.Now - brStart ).TotalMilliseconds;
					}

					if ( found )
					{
						entitiesToReveal.Add( entity, currentReason );
					}
				}

				DateTime reStart = DateTime.Now;
				if ( entitiesToReveal.Count > 0 )
					RevealEntities( entitiesToReveal );
				re += ( DateTime.Now - reStart ).TotalMilliseconds;

				if ( ( DateTime.Now - start ).TotalMilliseconds > 2000 && PluginSettings.Instance.DynamicShowMessages )
					Essentials.Log.Info( "Completed Reveal Check: {0}ms (br: {1}ms, re: {2}ms)", ( DateTime.Now - start ).TotalMilliseconds, br, re );
			}
			catch ( InvalidOperationException ex )
			{
				if ( ex.Message.StartsWith( "Collection was modified" ) )
					Essentials.Log.Trace( ex );
			}
			catch ( Exception ex )
			{
				Essentials.Log.Error( ex );
			}
			finally
			{
				_checkReveal = false;
			}
		}

		private static bool CheckRevealBlockRules( IMyCubeGrid grid, List<IMyPlayer> players, out string reason )
		{
			reason = "";
			// This is actually faster, but doesn't include power checks

			// Live dangerously
			List<IMySlimBlock> blocks = new List<IMySlimBlock>( );
			grid.GetBlocks( blocks, x => x.FatBlock != null );
			//CubeGrids.GetAllConnectedBlocks(_processedGrids, grid, blocks, x => x.FatBlock != null);
			//bool found = false;
			//bool powered = false;
			foreach ( IMySlimBlock block in blocks )
			{
				IMyCubeBlock cubeBlock = block.FatBlock;

				if ( cubeBlock.BlockDefinition.TypeId == typeof( MyObjectBuilder_Beacon ) )
				{
					//MyObjectBuilder_Beacon beacon = (MyObjectBuilder_Beacon)cubeBlock.GetObjectBuilderCubeBlock();
					IMyBeacon beacon = (IMyBeacon)cubeBlock;
					if ( !beacon.Enabled )
						continue;

					//Sandbox.ModAPI.Ingame.IMyFunctionalBlock functionalBlock = (Sandbox.ModAPI.Ingame.IMyFunctionalBlock)cubeBlock;
					//if (!functionalBlock.Enabled)
					//	continue;

					//Console.WriteLine("Beacon: {0} {1} {2}", beacon.BroadcastRadius, terminalBlock.IsWorking, terminalBlock.IsFunctional);
					//if (!terminalBlock.IsWorking)
					//	continue;


					foreach ( IMyPlayer player in players )
					{
						double distance;
						if ( Entity.GetDistanceBetweenPointAndPlayer( grid.GetPosition( ), player, out distance ) )
						{
							if ( distance < beacon.Radius )
							{
								//found = true;
								//break;
								reason = string.Format( "{0} distance to beacon broadcast: {1}", player.DisplayName, distance );
								return true;
							}
						}
					}
				}

				if ( cubeBlock.BlockDefinition.TypeId == typeof( MyObjectBuilder_RadioAntenna ) )
				{
					//MyObjectBuilder_RadioAntenna antenna = (MyObjectBuilder_RadioAntenna)cubeBlock.GetObjectBuilderCubeBlock();
					IMyRadioAntenna antenna = (IMyRadioAntenna)cubeBlock;

					if ( !antenna.Enabled )
						continue;

					//Sandbox.ModAPI.Ingame.IMyFunctionalBlock functionalBlock = (Sandbox.ModAPI.Ingame.IMyFunctionalBlock)cubeBlock;
					//if (!functionalBlock.Enabled)
					//	continue;

					foreach ( IMyPlayer player in players )
					{
						double distance = 0d;
						if ( Entity.GetDistanceBetweenPointAndPlayer( grid.GetPosition( ), player, out distance ) )
						{
							if ( distance < antenna.Radius )
							{
								//found = true;
								//break;
								reason = string.Format( "{0} distance to antenna broadcast: {1}", player.DisplayName, distance );
								return true;
							}
						}
					}
				}

				if ( cubeBlock.BlockDefinition.TypeId == typeof( MyObjectBuilder_MedicalRoom ) )
				{
					//MyObjectBuilder_MedicalRoom medical = (MyObjectBuilder_MedicalRoom)cubeBlock.GetObjectBuilderCubeBlock();
					IMyMedicalRoom medical = (IMyMedicalRoom)cubeBlock;
					if ( !medical.Enabled )
						continue;

					IMyFunctionalBlock functionalBlock = (IMyFunctionalBlock)cubeBlock;
					if ( !functionalBlock.IsFunctional )
						continue;

					//if (!functionalBlock.Enabled)
					//	continue;

					if ( PluginSettings.Instance.DynamicConcealIncludeMedBays )
					{
						lock ( Online )
						{
							foreach ( ulong connectedPlayer in Online )
							{
								long playerId = PlayerMap.Instance.GetFastPlayerIdFromSteamId( connectedPlayer );
								//if (medical.Owner == playerId || (medical.ShareMode == MyOwnershipShareModeEnum.Faction && Player.CheckPlayerSameFaction(medical.Owner, playerId)))
								if ( functionalBlock.OwnerId == playerId )
								{
									reason = string.Format( "Grid has medbay and player is logged in - playerid: {0}", playerId );
                                    //return true;

                                    //medbay is up for reveal, put it into the medbay queue to be revealed before other grids
                                    RevealMedbays( (IMyEntity)grid, reason );
                                    //return false so this grid doesn't get duplicated in the regular queue
                                    return false;
                                }

								if ( functionalBlock.GetUserRelationToOwner( playerId ) == MyRelationsBetweenPlayerAndBlock.FactionShare )
								{
									reason = string.Format( "Grid has medbay and player is factionshare - playerid: {0}", playerId );
                                    //return true;

                                    //medbay is up for reveal, put it into the medbay queue to be revealed before other grids
                                    RevealMedbays( (IMyEntity)grid, reason );
                                    //return false so this grid doesn't get duplicated in the regular queue
                                    return false;
                                }
							}
						}

                        /*
						foreach (ulong connectedPlayer in PlayerManager.Instance.ConnectedPlayers)
						{
							long playerId = PlayerMap.Instance.GetFastPlayerIdFromSteamId(connectedPlayer);
							//if (medical.Owner == playerId || (medical.ShareMode == MyOwnershipShareModeEnum.Faction && Player.CheckPlayerSameFaction(medical.Owner, playerId)))
							//if (functionalBlock.OwnerId == playerId || (functionalBlock.GetUserRelationToOwner(playerId) == Sandbox.Common.MyRelationsBetweenPlayerAndBlock.FactionShare))
							if(medical.HasPlayerAccess(playerId))
							{
								reason = string.Format("Grid has medbay and player is logged in - playerid: {0}", playerId);
								return true;
							}
						}
						 */
                    }
					else
					{
						reason = string.Format( "Grid has medbay and conceal can not include medbays" );
                        //return true;

                        //medbay is up for reveal, put it into the medbay queue to be revealed before other grids
                        RevealMedbays( (IMyEntity)grid, reason );
                        //return false so this grid doesn't get duplicated in the regular queue
                        return false;
                    }
                }

                if ( cubeBlock.BlockDefinition.TypeId == typeof( MyObjectBuilder_CryoChamber) )
                {
                        MyCryoChamber cryo = (MyCryoChamber)cubeBlock;
                        if ( cryo.Pilot == null )
                            continue;
                        
                    if ( !cryo.IsFunctional )
                        continue;                    

                    if ( PluginSettings.Instance.DynamicConcealIncludeMedBays )
                    {
                        lock ( Online )
                        {
                            foreach ( ulong connectedPlayer in Online )
                            {
                                long playerId = PlayerMap.Instance.GetFastPlayerIdFromSteamId( connectedPlayer );

                                if (cryo.Pilot.GetPlayerIdentityId() == playerId )
                                {
                                    reason = string.Format( "Grid has cryopod and player is inside - playerid: {0}", playerId );
                                    //return true;

                                    //medbay is up for reveal, put it into the medbay queue to be revealed before other grids
                                    RevealMedbays( (IMyEntity)grid, reason );
                                    //return false so this grid doesn't get duplicated in the regular queue
                                    return false;
                                }

                                if ( cryo.HasPlayerAccess( playerId ) )
                                {
                                    reason = string.Format( "Grid has cryopod and player can use - playerid: {0}", playerId );
                                    //return true;

                                    //medbay is up for reveal, put it into the medbay queue to be revealed before other grids
                                    RevealMedbays( (IMyEntity)grid, reason );
                                    //return false so this grid doesn't get duplicated in the regular queue
                                    return false;
                                }
                            }
                        }
                    }
                    else
                    {
                        reason = string.Format( "Grid has cryopod and conceal can not include cryopods" );
                        //return true;

                        //medbay is up for reveal, put it into the medbay queue to be revealed before other grids
                        RevealMedbays( (IMyEntity)grid, reason );
                        //return false so this grid doesn't get duplicated in the regular queue
                        return false;
                    }
                }

                if ( cubeBlock.BlockDefinition.TypeId == typeof( MyObjectBuilder_ProductionBlock ) )
				{
					MyObjectBuilder_ProductionBlock production = (MyObjectBuilder_ProductionBlock)cubeBlock.GetObjectBuilderCubeBlock( );
					if ( !production.Enabled )
						continue;

					IMyProductionBlock productionBlock = (IMyProductionBlock)cubeBlock;
					if ( production.Queue.Length > 0 )
					{
						reason = string.Format( "Grid has production facility that has a queue" );
						return true;
					}
				}
			}

			return false;
		}

		private static bool CheckRevealMedbay( IMyCubeGrid grid, ulong steamId )
		{
			// Live dangerously
			List<IMySlimBlock> blocks = new List<IMySlimBlock>( );
			grid.GetBlocks( blocks, x => x.FatBlock != null );
                        long playerId = PlayerMap.Instance.GetFastPlayerIdFromSteamId( steamId );
			foreach ( IMySlimBlock block in blocks )
			{
				IMyCubeBlock cubeBlock = block.FatBlock;

                if ( cubeBlock.BlockDefinition.TypeId == typeof( MyObjectBuilder_MedicalRoom ) )
                {
                    IMyMedicalRoom medical = (IMyMedicalRoom)cubeBlock;
                    if ( !medical.Enabled )
                        continue;

                    //if (medical.Owner == playerId || (medical.ShareMode == MyOwnershipShareModeEnum.Faction && Player.CheckPlayerSameFaction(medical.Owner, playerId)))
                    if ( medical.HasPlayerAccess( playerId ) )
                    {
                        return true;
                    }
                }

                if ( cubeBlock.BlockDefinition.TypeId == typeof( MyObjectBuilder_CryoChamber ) )
                {
                    MyCryoChamber cryo = (MyCryoChamber)cubeBlock;
                    if ( cryo.Pilot == null )
                        continue;

                    if ( cryo.HasPlayerAccess( playerId ) )
                    {
                        return true;
                    }
                }
            }

            return false;
		}

        private static void RevealEntities( Dictionary<IMyEntity, string> entitiesToReveal )
        {
            int RevealCount = 0;

            foreach ( KeyValuePair<IMyEntity, string> entity in entitiesToReveal )
            {
                
                IMyEntity processEntity = entity.Key;
                ConcealItem item = new ConcealItem( processEntity, entity.Value );

                if ( ConcealQueue.ContainsKey( processEntity.EntityId ) )
                {
                    //if this entity is in the queue, we want to replace it
                    ConcealQueue.Remove( processEntity.EntityId );
                }

                if ( MedbayQueue.ContainsKey( processEntity.EntityId ) )
                    continue;
                //skip this entity if it's already in the medbay queue, since it has a higher priority
                
                ++RevealCount;
            }

            RevealQueue.Clear( );
            foreach ( KeyValuePair<IMyEntity, string> entity in entitiesToReveal )
                RevealQueue.Add( entity.Key.EntityId, new ConcealItem( entity ) );

                if ( PluginSettings.Instance.DynamicShowMessages )
                    Essentials.Log.Info( "Queued {0} entities for reveal.", RevealCount );
        }

        private static void RevealMedbays( IMyEntity entity, string reason )
        {

            ConcealItem item = new ConcealItem( entity, reason );

            if ( ConcealQueue.ContainsKey( entity.EntityId ) )
                ConcealQueue.Remove( entity.EntityId );
            //if this entity is in the concealment queue, remove it

            if ( RevealQueue.ContainsKey( entity.EntityId ) )
                RevealQueue.Remove( entity.EntityId );
            //if this entity is in reveal queue, remove it. medbay is priority 0

            if ( MedbayQueue.ContainsKey( entity.EntityId ) )
                return;
            //we don't want dupes in the queue

            MedbayQueue.Add( entity.EntityId, item );            
        }

        public static void RevealEntity( KeyValuePair<IMyEntity, string> item )
		{
			IMyEntity entity = item.Key;
			string reason = item.Value;
			//Wrapper.GameAction(() =>
			//{
			MyObjectBuilder_CubeGrid builder = CubeGrids.SafeGetObjectBuilder( (IMyCubeGrid)entity );
			if ( builder == null )
				return;

            if ( entity.InScene )
                return;

			IMyCubeGrid grid = (IMyCubeGrid)entity;
			long ownerId = 0;
			string ownerName = string.Empty;
			if ( CubeGrids.GetBigOwners( builder ).Count > 0 )
			{
				ownerId = CubeGrids.GetBigOwners( builder ).First( );
				ownerName = PlayerMap.Instance.GetPlayerItemFromPlayerId( ownerId ).Name;
			}
			/*
			entity.InScene = true;
			entity.CastShadows = true;
			entity.Visible = true;
			*/

			builder.PersistentFlags = ( MyPersistentEntityFlags2.InScene | MyPersistentEntityFlags2.CastShadows );
			MyAPIGateway.Entities.RemapObjectBuilder( builder );
			//builder.EntityId = 0;

			if ( RemovedGrids.Contains( entity.EntityId ) )
			{
				Essentials.Log.Info( "Revealing - Id: {0} DUPE FOUND Display: {1} OwnerId: {2} OwnerName: {3}  Reason: {4}",
									 entity.EntityId,
									 entity.DisplayName.Replace( "\r", "" ).Replace( "\n", "" ),
									 ownerId,
									 ownerName,
									 reason );
				BaseEntityNetworkManager.BroadcastRemoveEntity( entity, false );
                MyMultiplayer.ReplicateImmediatelly( MyExternalReplicable.FindByObject( entity ) );
            }
			else
			{
				if ( !PluginSettings.Instance.DynamicConcealServerOnly )
				{
					IMyEntity newEntity = MyAPIGateway.Entities.CreateFromObjectBuilder( builder );
					if ( newEntity == null )
					{
						Essentials.Log.Warn( "CreateFromObjectBuilder failed: {0}", builder.EntityId );
						return;
					}

					RemovedGrids.Add( entity.EntityId );
                    entity.InScene = true;
                    entity.OnAddedToScene( entity );
                    BaseEntityNetworkManager.BroadcastRemoveEntity( entity, false );
                    MyAPIGateway.Entities.AddEntity( newEntity );
                    MyMultiplayer.ReplicateImmediatelly( MyExternalReplicable.FindByObject( newEntity ) );
                    entity.Physics.LinearVelocity = Vector3.Zero;
                    entity.Physics.AngularVelocity = Vector3.Zero;

                    if ( PluginSettings.Instance.DynamicShowMessages )
						Essentials.Log.Info( "Revealed - Id: {0} -> {4} Display: {1} OwnerId: {2} OwnerName: {3}  Reason: {5}",
										 entity.EntityId,
										 entity.DisplayName.Replace( "\r", "" ).Replace( "\n", "" ),
										 ownerId,
										 ownerName,
										 newEntity.EntityId,
										 reason );
				}
				else
				{
					entity.InScene = true;
					// Send to users, client will remove if doesn't need - this solves login problem
					/*CC
						if (PluginSettings.Instance.DynamicClientConcealEnabled)
						{
							ClientEntityManagement.AddEntityState(entity.EntityId);
							List<MyObjectBuilder_EntityBase> addList = new List<MyObjectBuilder_EntityBase>();
							addList.Add(entity.GetObjectBuilder());
					MyAPIGateway.Multiplayer.SendEntitiesCreated(addList);
						}
						*/
					if ( PluginSettings.Instance.DynamicShowMessages )
						Essentials.Log.Info( "Revealed - Id: {0} -> {4} Display: {1} OwnerId: {2} OwnerName: {3}  Reason: {4}",
										 entity.EntityId,
										 entity.DisplayName.Replace( "\r", "" ).Replace( "\n", "" ),
										 ownerId,
										 ownerName,
										 reason );
				}
			}
			//});
		}

		static public void RevealAll( )
		{
            ConcealQueue.Clear( );
            RevealQueue.Clear( );

            ProcessConceal.ForceReveal = true;
            //set the force flag

			HashSet<IMyEntity> entities = new HashSet<IMyEntity>( );
			Wrapper.GameAction( ( ) =>
			{
				MyAPIGateway.Entities.GetEntities( entities );
			} );

			List<MyObjectBuilder_EntityBase> addList = new List<MyObjectBuilder_EntityBase>( );
            int count = 0;
            foreach ( IMyEntity entity in entities )
            {
                if ( entity.InScene )
                    continue;

                if ( !(entity is IMyCubeGrid) )
                    continue;

                MyObjectBuilder_CubeGrid builder = CubeGrids.SafeGetObjectBuilder( (IMyCubeGrid)entity );
                if ( builder == null )
                    continue;

                long itemId = entity.EntityId;
                ConcealItem item = new ConcealItem( entity, "Force reveal" );
                if ( MedbayQueue.ContainsKey( itemId ) )
                    continue;

                count++;

                RevealQueue.Add( entity.EntityId, item );
            }

            if ( PluginSettings.Instance.DynamicShowMessages )
				Essentials.Log.Info( "Queued {0} grids for force reveal.", count );
		}

		public static bool ToggleMedbayGrids( ulong steamId )
		{
			if ( _checkConceal || _checkReveal )
			{
				Communication.SendPrivateInformation( steamId, "Server busy" );
				return false;
			}

			_checkConceal = true;
			_checkReveal = true;
			try
			{
				DateTime start = DateTime.Now;

				// Toggle off
				HashSet<IMyEntity> entities = new HashSet<IMyEntity>( );
				HashSet<IMyEntity> entitiesFound = new HashSet<IMyEntity>( );

				try
				{
                    Wrapper.GameAction( ( ) =>
                     {
                         MyAPIGateway.Entities.GetEntities( entities );
                     } );
				}
				catch
				{
					Essentials.Log.Info( "Error getting entity list, skipping check" );
					return false;
				}

				CubeGrids.GetGridsUnconnected( entitiesFound, entities );

				HashSet<IMyEntity> entitiesToConceal = new HashSet<IMyEntity>( );
				FilterEntitiesForMedbayCheck( steamId, entitiesFound, entitiesToConceal );

				if ( entitiesToConceal.Count > 0 )
				{
					//if (PluginSettings.Instance.DynamicClientConcealEnabled)
					//{
					//ClientEntityManagement.Refresh(steamId);
					//}

					//Communication.WaypointMessage( steamId, string.Format( "/conceal {0}", string.Join( ",", entitiesToConceal.Select( x => x.EntityId.ToString( ) + ":" + ( (MyObjectBuilder_CubeGrid)x.GetObjectBuilder( ) ).CubeBlocks.Count.ToString( ) + ":" + x.DisplayName ).ToArray( ) ) ) );
					Thread.Sleep( 1500 );
                    ConcealEntities( entitiesToConceal );
                    //CheckAndRevealEntities();
                }

				if ( ( DateTime.Now - start ).TotalMilliseconds > 2000 && PluginSettings.Instance.DynamicShowMessages )
					Essentials.Log.Info( "Completed Toggle: {0}ms", ( DateTime.Now - start ).TotalMilliseconds );
			}
			catch ( Exception ex )
			{
				Essentials.Log.Error( ex );
			}
			finally
			{
				_checkConceal = false;
				_checkReveal = false;
			}

			return true;
		}

		private static void FilterEntitiesForMedbayCheck( ulong steamId, HashSet<IMyEntity> entitiesFound, HashSet<IMyEntity> entitiesToConceal )
		{
			foreach ( IMyEntity entity in entitiesFound )
			{
				if ( !( entity is IMyCubeGrid ) )
				{
					continue;
				}
                
				if ( !entity.InScene )
				{
					continue;
				}

				if ( ( (IMyCubeGrid)entity ).GridSizeEnum != MyCubeSize.Small && !PluginSettings.Instance.ConcealIncludeLargeGrids )
				{
					continue;
				}

				IMyCubeGrid grid = (IMyCubeGrid)entity;
				long playerId = PlayerMap.Instance.GetFastPlayerIdFromSteamId( steamId );
				if ( !grid.BigOwners.Contains( playerId ) && !grid.SmallOwners.Contains( playerId ) )
				{
					continue;
				}

				bool found = false;
				// Check to see if grid is close to dock / shipyard
				foreach ( IMyCubeGrid checkGrid in ProcessDockingZone.ZoneCache )
				{
					try
					{
						if ( Vector3D.Distance( checkGrid.GetPosition( ), grid.GetPosition( ) ) < 100d )
						{
							found = true;
							break;
						}
					}
					catch
					{
						continue;
					}
				}

				if ( !found )
				{
					// Check for block type rules
				}

				if ( !found )
				{
					entitiesToConceal.Add( entity );
				}
			}
		}

		public static void SetOnline( ulong steamId, bool online )
		{
			lock ( Online )
			{
				if ( online )
				{
					if ( !Online.Contains( steamId ) )
					{
						Online.Add( steamId );
					}
				}
				else
				{
					if ( Online.Contains( steamId ) )
					{
						Online.Remove( steamId );
					}
				}
			}
		}
	}
}
