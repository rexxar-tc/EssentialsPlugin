﻿namespace EssentialsPlugin.ChatHandlers
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using EssentialsPlugin.Utility;
	using Sandbox.Common.ObjectBuilders;
	using Sandbox.Game.Entities;
	using Sandbox.Game.Entities.Cube;
	using Sandbox.ModAPI;
	using Sandbox.ModAPI.Ingame;
	using SEModAPIInternal.API.Common;
	using SEModAPIInternal.API.Entity.Sector.SectorObject;
	using VRage.Game;
	using VRage.Game.Entity;
	using VRage.Game.ModAPI;
	using VRage.ModAPI;

	public class HandleAdminScanNoBeacon : ChatHandlerBase
	{
		public override string GetHelp()
		{
			return "This command allows you to scan all grids that do not have beacons.  Takes into account if a grid is connected to other grids.  Usage: /admin scan nobeacon";
		}
		public override string GetCommandText()
		{
			return "/admin scan nobeacon";
		}

        public override Communication.ServerDialogItem GetHelpDialog( )
        {
            return new Communication.ServerDialogItem
            {
                title = "Help",
                header = "/admin scan nobeacon",
                buttonText = "close",
                content = "This command will scan for grids without a beacon. ||" +
                "If you run this command with the 'physical' argument, grids without a beacon attached" +
                "by landing gear to another grid wich does have a beacon will not be counted. ||" +
                "Usage: /admin scan nobeacon (physical)"
            };
        }

        public override bool IsAdminCommand()
		{
			return true;
		}

		public override bool AllowedInConsole()
		{
			return true;
		}

		// admin nobeacon scan
		public override bool HandleCommand(ulong userId, string[] words)
		{
            GridLinkTypeEnum connectionType = GridLinkTypeEnum.Logical;
		    if ( words.Length > 0 && words[0].ToLower( ) == "physical" )
		        connectionType = GridLinkTypeEnum.Physical;

		    try
		    {
		        HashSet<GridGroup> groups = GridGroup.GetAllGroups( connectionType );
		        int groupsCount = 0;
		        int gridsCount = 0;

		        foreach ( var group in groups )
		        {
		            if ( !group.CubeBlocks.Any( x => x?.FatBlock is IMyBeacon ) )
		            {
		                groupsCount++;
		                gridsCount += group.Grids.Count;
		                Communication.SendPrivateInformation( userId, $"Found group with parent {group.Parent.DisplayName} ({group.Parent.EntityId}) at {group.Parent.PositionComp.GetPosition( )} with no beacon." );
		            }
		        }

		        Communication.SendPrivateInformation( userId, $"Found {gridsCount} grids in {groupsCount} groups with no beacon." );
		    }
		    catch ( Exception ex )
		    {
		        Log.Info( string.Format( "Scan error: {0}", ex.ToString( ) ) );
		    }

		    return true;
		}
	}
}
