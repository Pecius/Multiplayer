﻿using LiteNetLib;
using Multiplayer.Common;
using RimWorld;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;
using UnityEngine;
using Verse;
using Verse.Steam;

namespace Multiplayer.Client
{
    [StaticConstructorOnStartup]
    public class ServerBrowser : Window
    {
        private NetManager net;
        private List<LanServer> servers = new List<LanServer>();

        public override Vector2 InitialSize => new Vector2(800f, 500f);

        public ServerBrowser()
        {
            EventBasedNetListener listener = new EventBasedNetListener();
            listener.NetworkReceiveUnconnectedEvent += (endpoint, data, type) =>
            {
                Log.Message("receive lan");

                if (type != UnconnectedMessageType.DiscoveryRequest) return;

                string s = Encoding.UTF8.GetString(data.GetRemainingBytes());
                if (s == "mp-server")
                    AddOrUpdate(endpoint);
            };

            net = new NetManager(listener);
            net.DiscoveryEnabled = true;
            net.ReuseAddress = true;
            net.Start(5100);

            doCloseX = true;
            closeOnAccept = false;

            ReloadFiles();
        }

        private Vector2 lanScroll;
        private Vector2 steamScroll;
        private Vector2 hostScroll;
        private static Tabs tab;

        enum Tabs
        {
            Lan, Direct, Steam, Host
        }

        public override void DoWindowContents(Rect inRect)
        {
            List<TabRecord> tabs = new List<TabRecord>()
            {
                new TabRecord("LAN", () => tab = Tabs.Lan,  tab == Tabs.Lan),
                new TabRecord("Direct", () => tab = Tabs.Direct, tab == Tabs.Direct),
                new TabRecord("Steam", () => tab = Tabs.Steam, tab == Tabs.Steam),
                new TabRecord("Host", () => tab = Tabs.Host, tab == Tabs.Host),
            };

            inRect.yMin += 35f;
            TabDrawer.DrawTabs(inRect, tabs);

            GUI.BeginGroup(new Rect(0, inRect.yMin, inRect.width, inRect.height));
            {
                Rect groupRect = new Rect(0, 0, inRect.width, inRect.height);

                if (tab == Tabs.Lan)
                    DrawLan(groupRect);
                else if (tab == Tabs.Direct)
                    DrawDirect(groupRect);
                else if (tab == Tabs.Steam)
                    DrawSteam(groupRect);
                else if (tab == Tabs.Host)
                    DrawHost(groupRect);
            }
            GUI.EndGroup();
        }

        private List<SaveFile> spSaves = new List<SaveFile>();
        private List<SaveFile> mpReplays = new List<SaveFile>();

        private void ReloadFiles()
        {
            foreach (FileInfo file in GenFilePaths.AllSavedGameFiles)
            {
                spSaves.Add(new SaveFile(Path.GetFileNameWithoutExtension(file.Name), false, file, GetWorldName(file)));
            }

            var replaysDir = new DirectoryInfo(GenFilePaths.FolderUnderSaveData("MpReplays"));
            if (!replaysDir.Exists)
                replaysDir.Create();

            foreach (var file in replaysDir.GetFiles().Where(f => f.Extension == ".zip").OrderByDescending(f => f.LastWriteTime))
            {
                var replay = Replay.ForLoading(file);
                replay.LoadInfo();
                mpReplays.Add(new SaveFile(Path.GetFileNameWithoutExtension(file.Name), true, file, replay.info.name));
            }
        }

        private string GetWorldName(FileInfo file)
        {
            using (var stream = new StreamReader(file.FullName))
            {
                using (var reader = new XmlTextReader(stream))
                {
                    var result =
                        reader.
                        ReadToNextElement()?. // savedGame
                        ReadToNextElement()?. // meta
                        SkipContents().
                        ReadToNextElement()?. // game
                        ReadToNextElement("world")?.
                        ReadToNextElement("info")?.
                        ReadToNextElement("name")?.
                        ReadFirstText();

                    return result;
                }
            }
        }

        private bool mpCollapsed, spCollapsed;
        private float hostHeight;

        private SaveFile selectedFile;
        private float fileButtonsWidth;

        private void DrawHost(Rect inRect)
        {
            inRect.y += 8;

            float margin = 80;
            Rect outRect = new Rect(margin, inRect.yMin + 10, inRect.width - 2 * margin, inRect.height - 80);
            Rect viewRect = new Rect(0, 0, outRect.width - 16f, hostHeight);

            Widgets.BeginScrollView(outRect, ref hostScroll, viewRect, true);

            Rect collapseRect = new Rect(0, 4f, 18f, 18f);
            if (Widgets.ButtonImage(collapseRect, mpCollapsed ? TexButton.Reveal : TexButton.Collapse))
                mpCollapsed = !mpCollapsed;

            float y = 0;
            Text.Font = GameFont.Medium;
            float textHeight1 = Text.CalcHeight("Multiplayer", inRect.width);
            Widgets.Label(viewRect.Right(18f), "Multiplayer");
            Text.Font = GameFont.Small;
            y += textHeight1 + 10;

            if (!mpCollapsed)
            {
                DrawSaveList(mpReplays, viewRect.width, ref y);
                y += 25;
            }

            collapseRect.y += y;

            if (Widgets.ButtonImage(collapseRect, spCollapsed ? TexButton.Reveal : TexButton.Collapse))
                spCollapsed = !spCollapsed;

            viewRect.y = y;
            Text.Font = GameFont.Medium;
            float textHeight2 = Text.CalcHeight("Singleplayer", inRect.width);
            Widgets.Label(viewRect.Right(18), "Singleplayer");
            Text.Font = GameFont.Small;
            y += textHeight2 + 10;

            if (!spCollapsed)
                DrawSaveList(spSaves, viewRect.width, ref y);

            if (Event.current.type == EventType.layout)
                hostHeight = y;

            Widgets.EndScrollView();

            if (selectedFile == null)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(outRect.x, outRect.yMax, outRect.width, 80), "Nothing selected");
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                float width = 0;

                GUI.BeginGroup(new Rect(outRect.x + (outRect.width - fileButtonsWidth) / 2, outRect.yMax + 20, fileButtonsWidth, 40));
                DrawFileButtons(selectedFile, ref width);
                GUI.EndGroup();

                if (Event.current.type == EventType.layout)
                {
                    fileButtonsWidth = width;
                }
            }
        }

        private void DrawFileButtons(SaveFile file, ref float width)
        {
            if (file.replay)
            {
                if (Widgets.ButtonText(new Rect(width, 0, 120, 40), "Watch"))
                {
                    Replay.LoadReplay(file.name);
                }

                width += 120 + 10;
            }

            if (Widgets.ButtonText(new Rect(width, 0, 120, 40), "Host"))
            {
                Find.WindowStack.Add(new HostWindow(file) { returnToServerBrowser = true });
                Close(false);
            }

            width += 120 + 10;

            if (Widgets.ButtonText(new Rect(width, 0, 120, 40), "Delete"))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmDelete".Translate(file.name), () =>
                {
                    file.file.Delete();
                    ReloadFiles();
                }, true));
            }

            width += 120;
        }

        private void DrawSaveList(List<SaveFile> saves, float width, ref float y)
        {
            for (int i = 0; i < saves.Count; i++)
            {
                var saveFile = saves[i];
                Rect entryRect = new Rect(0, y, width, 40);

                if (saveFile == selectedFile)
                {
                    Widgets.DrawRectFast(entryRect, new Color(1f, 1f, 0.7f, 0.1f));

                    var lineColor = new Color(1, 1, 1, 0.3f);
                    Widgets.DrawLine(entryRect.min, entryRect.TopRightCorner(), lineColor, 2f);
                    Widgets.DrawLine(entryRect.min + new Vector2(2, 1), entryRect.BottomLeftCorner() + new Vector2(2, -1), lineColor, 2f);
                    Widgets.DrawLine(entryRect.BottomLeftCorner(), entryRect.max, lineColor, 2f);
                    Widgets.DrawLine(entryRect.TopRightCorner() - new Vector2(2, -1), entryRect.max - new Vector2(2, 1), lineColor, 2f);
                }
                else if (i % 2 == 0)
                {
                    Widgets.DrawAltRect(entryRect);
                }

                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(entryRect.Right(10), saveFile.name);
                Text.Anchor = TextAnchor.UpperLeft;

                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                Text.Font = GameFont.Tiny;
                var infoText = new Rect(entryRect.xMax - 120, entryRect.yMin + 3, 120, entryRect.height);
                Widgets.Label(infoText, saveFile.gameName.Truncate(110));
                Widgets.Label(infoText.Down(16), saveFile.file.LastWriteTime.ToString("g"));
                Text.Font = GameFont.Small;
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(entryRect))
                    selectedFile = saveFile;

                y += 40;
            }
        }

        private bool ButtonImage(Rect rect, Texture2D image, Color imageColor, Vector2? imageSize)
        {
            bool result = Widgets.ButtonText(rect, string.Empty, true, false, true);
            Rect position;
            if (imageSize != null)
                position = new Rect(Mathf.Floor(rect.x + rect.width / 2f - imageSize.Value.x / 2f), Mathf.Floor(rect.y + rect.height / 2f - imageSize.Value.y / 2f), imageSize.Value.x, imageSize.Value.y);
            else
                position = rect;

            GUI.color = Color.black;
            GUI.DrawTexture(position.Down(1).Right(1), image);
            GUI.color = imageColor;
            GUI.DrawTexture(position, image);
            GUI.color = Color.white;

            return result;
        }

        private List<SteamPersona> friends = new List<SteamPersona>();
        private static readonly Color SteamGreen = new Color32(144, 186, 60, 255);

        private void DrawSteam(Rect inRect)
        {
            string info = null;
            if (!SteamManager.Initialized)
                info = "Not connected to Steam";
            else if (friends.Count == 0)
                info = "No friends currently playing RimWorld";

            if (info != null)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(0, 8, inRect.width, 40f), info);

                Text.Anchor = TextAnchor.UpperLeft;
                inRect.yMin += 40f;
            }

            float margin = 80;
            Rect outRect = new Rect(margin, inRect.yMin + 10, inRect.width - 2 * margin, inRect.height - 20);

            float height = friends.Count * 40;
            Rect viewRect = new Rect(0, 0, outRect.width - 16f, height);

            Widgets.BeginScrollView(outRect, ref steamScroll, viewRect, true);

            float y = 0;
            int i = 0;

            foreach (SteamPersona friend in friends)
            {
                Rect entryRect = new Rect(0, y, viewRect.width, 40);
                if (i % 2 == 0)
                    Widgets.DrawAltRect(entryRect);

                if (Event.current.type == EventType.repaint)
                    GUI.DrawTextureWithTexCoords(new Rect(5, entryRect.y + 4, 32, 32), SteamImages.GetTexture(friend.avatar), new Rect(0, 1, 1, -1));

                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(entryRect.Right(45).Up(5), friend.username);

                GUI.color = SteamGreen;
                Text.Font = GameFont.Tiny;
                Widgets.Label(entryRect.Right(45).Down(8), "Playing RimWorld");
                Text.Font = GameFont.Small;
                GUI.color = Color.white;

                Text.Anchor = TextAnchor.MiddleCenter;

                if (friend.serverHost != CSteamID.Nil)
                {
                    Rect playButton = new Rect(entryRect.xMax - 85, entryRect.y + 5, 80, 40 - 10);
                    if (Widgets.ButtonText(playButton, "Join"))
                    {
                        Close(false);

                        Find.WindowStack.Add(new SteamConnectingWindow(friend.serverHost) { returnToServerBrowser = true });

                        SteamConnection conn = new SteamConnection(friend.serverHost);
                        conn.username = Multiplayer.username;
                        Multiplayer.session = new MultiplayerSession();
                        Multiplayer.session.client = conn;
                        conn.State = ConnectionStateEnum.ClientSteam;
                    }
                }
                else
                {
                    Rect playButton = new Rect(entryRect.xMax - 125, entryRect.y + 5, 120, 40 - 10);
                    Widgets.ButtonText(playButton, "Not in multiplayer", false, false, false);
                }

                Text.Anchor = TextAnchor.UpperLeft;

                y += entryRect.height;
                i++;
            }

            Widgets.EndScrollView();
        }

        private string ip = "127.0.0.1";

        private void DrawDirect(Rect inRect)
        {
            ip = Widgets.TextField(new Rect(inRect.center.x - 200 / 2, 15f, 200, 35f), ip);

            if (Widgets.ButtonText(new Rect(inRect.center.x - 100f / 2, 60f, 100f, 35f), "Connect"))
            {
                int port = MultiplayerServer.DefaultPort;
                string[] ipport = ip.Split(':');
                if (ipport.Length == 2)
                    int.TryParse(ipport[1], out port);
                else
                    port = MultiplayerServer.DefaultPort;

                if (!IPAddress.TryParse(ipport[0], out IPAddress address))
                {
                    Messages.Message("Invalid IP address.", MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    Find.WindowStack.Add(new ConnectingWindow(address, port) { returnToServerBrowser = true });
                    Close(false);
                }
            }
        }

        private void DrawLan(Rect inRect)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(inRect.x, 8f, inRect.width, 40), "Searching" + MpUtil.FixedEllipsis());
            Text.Anchor = TextAnchor.UpperLeft;
            inRect.yMin += 40f;

            float margin = 100;
            Rect outRect = new Rect(margin, inRect.yMin + 10, inRect.width - 2 * margin, inRect.height - 20);

            float height = servers.Count * 40;
            Rect viewRect = new Rect(0, 0, outRect.width - 16f, height);

            Widgets.BeginScrollView(outRect, ref lanScroll, viewRect, true);

            float y = 0;
            int i = 0;

            foreach (LanServer server in servers)
            {
                Rect entryRect = new Rect(0, y, viewRect.width, 40);
                if (i % 2 == 0)
                    Widgets.DrawAltRect(entryRect);

                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(entryRect.Right(5), "" + server.endpoint);

                Text.Anchor = TextAnchor.MiddleCenter;
                Rect playButton = new Rect(entryRect.xMax - 75, entryRect.y + 5, 70, 40 - 10);
                if (Widgets.ButtonText(playButton, ">>"))
                {
                    Close(false);
                    Find.WindowStack.Add(new ConnectingWindow(server.endpoint.Address, server.endpoint.Port) { returnToServerBrowser = true });
                }

                Text.Anchor = TextAnchor.UpperLeft;

                y += entryRect.height;
                i++;
            }

            Widgets.EndScrollView();
        }

        public override void WindowUpdate()
        {
            UpdateLan();

            if (SteamManager.Initialized)
                UpdateSteam();
        }

        private void UpdateLan()
        {
            net.PollEvents();

            for (int i = servers.Count - 1; i >= 0; i--)
            {
                LanServer server = servers[i];
                if (Environment.TickCount - server.lastUpdate > 5000)
                    servers.RemoveAt(i);
            }
        }

        private int lastFriendUpdate = 0;

        private void UpdateSteam()
        {
            if (Environment.TickCount - lastFriendUpdate > 2000)
            {
                friends.Clear();

                int friendCount = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
                for (int i = 0; i < friendCount; i++)
                {
                    CSteamID friend = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                    SteamFriends.GetFriendGamePlayed(friend, out FriendGameInfo_t friendGame);
                    bool playingRimworld = friendGame.m_gameID.AppID() == Multiplayer.RimWorldAppId;
                    if (!playingRimworld) continue;

                    int avatar = SteamFriends.GetSmallFriendAvatar(friend);
                    string username = SteamFriends.GetFriendPersonaName(friend);
                    string connectValue = SteamFriends.GetFriendRichPresence(friend, "connect");

                    CSteamID serverHost = CSteamID.Nil;
                    if (connectValue != null &&
                        connectValue.Contains(Multiplayer.SteamConnectStart) &&
                        ulong.TryParse(connectValue.Substring(Multiplayer.SteamConnectStart.Length), out ulong hostId))
                    {
                        serverHost = (CSteamID)hostId;
                    }

                    friends.Add(new SteamPersona()
                    {
                        id = friend,
                        avatar = avatar,
                        username = username,
                        playingRimworld = playingRimworld,
                        serverHost = serverHost,
                    });
                }

                friends.SortByDescending(f => f.serverHost != CSteamID.Nil);

                lastFriendUpdate = Environment.TickCount;
            }
        }

        public override void PostClose()
        {
            Cleanup();
        }

        public void Cleanup(bool onMainThread = false)
        {
            WaitCallback stop = s => net.Stop();

            if (onMainThread)
                stop(null);
            else
                ThreadPool.QueueUserWorkItem(stop);
        }

        private void AddOrUpdate(IPEndPoint endpoint)
        {
            LanServer server = servers.Find(s => s.endpoint.Equals(endpoint));

            if (server == null)
            {
                servers.Add(new LanServer()
                {
                    endpoint = endpoint,
                    lastUpdate = Environment.TickCount
                });
            }
            else
            {
                server.lastUpdate = Environment.TickCount;
            }
        }

        class LanServer
        {
            public IPEndPoint endpoint;
            public int lastUpdate;
        }
    }

    public class SteamPersona
    {
        public CSteamID id;
        public string username;
        public int avatar;

        public bool playingRimworld;
        public CSteamID serverHost = CSteamID.Nil;
    }

    public class SaveFile
    {
        public string name;
        public bool replay;
        public FileInfo file;
        public string gameName;

        public SaveFile(string name, bool replay, FileInfo file, string gameName)
        {
            this.name = name;
            this.replay = replay;
            this.file = file;
            this.gameName = gameName;
        }
    }

}
