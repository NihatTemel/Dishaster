using Mirror;
using Steamworks;

namespace Dishaster
{
    /// <summary>Lobby sahnesindeki oyuncu: isim, tip ve hazır durumu. Sahne geçişinde DishasterPlayer ile değişiyor.</summary>
    public class DishasterRoomPlayer : NetworkRoomPlayer
    {
        public const int NoRole = -1;

        [SyncVar] public string playerName = "";
        [SyncVar] public int monkeyRole = NoRole;

        public string DisplayName => string.IsNullOrEmpty(playerName) ? "Player " + (index + 1) : playerName;

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();

            // Steam adını sadece oyuncunun kendi makinesi biliyor.
            var manager = DishasterNetworkManager.Instance;
            if (manager != null && manager.SteamReady)
                CmdSetName(SteamFriends.GetPersonaName());
        }

        [Command]
        void CmdSetName(string value)
        {
            playerName = value;
        }

        /// <summary>Tip seçimi. Her tipi tek oyuncu alabilir; çakışmayı sunucu çözüyor.</summary>
        [Command]
        public void CmdSelectRole(int role)
        {
            if (role < 0 || role >= MonkeyRoles.Count || readyToBegin) return;

            foreach (NetworkRoomPlayer player in DishasterNetworkManager.Instance.roomSlots)
                if (player != this && player is DishasterRoomPlayer other && other.monkeyRole == role)
                    return;

            monkeyRole = role;
        }
    }
}
