﻿namespace EssentialsPlugin.ProcessHandlers
{
    using System;
    using Entities.Blocks;
    using EssentialsPlugin;
    using EssentialsPlugin.ChatHandlers;
    using EssentialsPlugin.EntityManagers;
    using Sandbox.Game.Entities.Blocks;

    public class ProcessConceal : ProcessHandlerBase
	{
		private static DateTime m_lastConcealCheck;
		private static DateTime m_lastRevealCheck;
        private static DateTime m_lastConcealProcess;
        //private static int updateSpeed = PluginSettings.Instance.DynamicConcealUpdateSpeed;
        public static bool ForceReveal = false;

        public static DateTime LastRevealCheck
		{
			get { return m_lastRevealCheck; }
			set { m_lastRevealCheck = value; }
		}

		public ProcessConceal()
		{
			//EntityManagement.RevealAll();
		}

		public override int GetUpdateResolution()
        {
            int updateSpeed = PluginSettings.Instance.DynamicConcealUpdateSpeed;

            if ( updateSpeed < 100 )
                return updateSpeed;
            else
                return 100;
        }

		public override void Handle()
		{
            int updateSpeed = PluginSettings.Instance.DynamicConcealUpdateSpeed;

            //force reveal bypasses the plugin setting. useful to recover grids after you turn it off
            if ( ForceReveal && DateTime.Now - m_lastConcealProcess > TimeSpan.FromMilliseconds( updateSpeed ) )
            {
                //Essentials.Log.Info( "Process force reveal" );
                EntityManagement.ProcessConcealment( true );
                m_lastConcealProcess = DateTime.Now;
            }

            if (!PluginSettings.Instance.DynamicConcealEnabled )
				return;

			if (DateTime.Now - m_lastConcealCheck > TimeSpan.FromSeconds(30))
			{
				//Essentials.Log.Info("CheckAndConcealEntities");
				EntityManagement.CheckAndConcealEntities();
				m_lastConcealCheck = DateTime.Now;
			}

			if (DateTime.Now - m_lastRevealCheck > TimeSpan.FromSeconds(5))
			{
				//Essentials.Log.Info("CheckAndRevealEntities");
				EntityManagement.CheckAndRevealEntities();
				m_lastRevealCheck = DateTime.Now;
			}

            if ( DateTime.Now - m_lastConcealProcess > TimeSpan.FromMilliseconds( updateSpeed ) )
            {
                //Essentials.Log.Info( "Process concealment" );
                EntityManagement.ProcessConcealment( );
                m_lastConcealProcess = DateTime.Now;
            }

			base.Handle();
		}

		public override void OnPlayerJoined(ulong remoteUserId)
		{
			if (!PluginSettings.Instance.DynamicConcealEnabled)
				return;

			if (HandleUtilityGridsRefresh.RefreshTrack.Contains(remoteUserId))
				HandleUtilityGridsRefresh.RefreshTrack.Remove(remoteUserId);

            EntityManagement.SetOnline( remoteUserId, true );

            if ( !PluginSettings.Instance.DynamicConcealEnabled )
                return;

            if ( DateTime.Now - m_lastRevealCheck > TimeSpan.FromSeconds( 2 ) )
            {
                EntityManagement.CheckAndRevealEntities( );
                m_lastRevealCheck = DateTime.Now;
            }
            
            Essentials.Log.Info( "Check Reveal due to: {0}", remoteUserId );

            base.OnPlayerJoined(remoteUserId);
		}

		public override void OnPlayerWorldSent(ulong remoteUserId)
		{
            //OnPlayerWorldSent doesn't seem to exist anymore. Doesn't hurt to leave this, though.
			EntityManagement.SetOnline(remoteUserId, true);

			if (!PluginSettings.Instance.DynamicConcealEnabled)
				return;

            if ( DateTime.Now - m_lastRevealCheck > TimeSpan.FromSeconds( 2 ) )
            {
                EntityManagement.CheckAndRevealEntities( );
                m_lastRevealCheck = DateTime.Now;
            }

            Essentials.Log.Info( "Check Reveal due to: {0}", remoteUserId );

			base.OnPlayerWorldSent(remoteUserId);
		}

		public override void OnPlayerLeft(ulong remoteUserId)
		{
			EntityManagement.SetOnline(remoteUserId, false);
			base.OnPlayerLeft(remoteUserId);
		}

	}
}

