namespace EssentialsPlugin.ChatHandlers.Admin
{
    using System.Collections.Generic;
    using System.ComponentModel.Design.Serialization;
    using Sandbox.Definitions;
    using Sandbox.Game.Entities;
    using Sandbox.Game.Entities.Cube;
    using Sandbox.ModAPI;
    using Utility;
    using VRage.Game.Entity;

    public class HandleAdminTest : ChatHandlerBase
	{

    public override string GetHelp()
		{
			return "For testing.";
		}

		public override string GetCommandText()
		{
			return "/admin test";
		}
        
        public override bool IsAdminCommand()
		{
			return true;
		}

		public override bool AllowedInConsole()
		{
			return true;
		}

        public override bool HandleCommand( ulong userId, string[ ] words )
        {
            var categories = MyDefinitionManager.Static.GetCategories( );
            int index = 0;
            foreach ( KeyValuePair<string, MyGuiBlockCategoryDefinition> kvp in categories )
            {
                Essentials.Log.Warn( $"===================={index}====================" );
                Essentials.Log.Error( kvp.Key );
                foreach(string itemId in kvp.Value.ItemIds)
                    Essentials.Log.Info( itemId );
                index++;
            }
            HashSet<MyEntity>entities = new HashSet<MyEntity>();
            Wrapper.GameAction( ()=> entities = MyEntities.GetEntities(  ) );
            foreach ( var entity in entities )
            {
                if ( entity is MyCubeGrid )
                {
                    foreach ( MySlimBlock slimblock in ( (MyCubeGrid)entity ).CubeBlocks )
                    {
                        MyCubeBlock block = ( slimblock?.FatBlock as MyCubeBlock );
                        if ( block == null )
                            continue;
                        Essentials.Log.Warn( slimblock.BlockDefinition.Id.ToString );
                        Essentials.Log.Warn( slimblock.BlockDefinition.Id.TypeId.ToString );
                        Essentials.Log.Warn( slimblock.BlockDefinition.Id.SubtypeId.ToString );
                        return true;
                    }
                }
            }
            return true;
        }

	}

}

