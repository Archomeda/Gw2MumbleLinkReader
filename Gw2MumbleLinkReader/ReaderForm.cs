using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using Gw2Sharp;
using Gw2Sharp.Models;
using Gw2Sharp.Mumble;
using Gw2Sharp.WebApi.V2.Models;

namespace MumbleLinkReader
{
    public partial class ReaderForm : Form
    {
        private readonly Gw2Client client = new();

        private Thread? apiThread;
        private Thread? mumbleLoopThread;
        private bool stopRequested;

        private readonly Queue<int> apiMapDownloadQueue = new();
        private readonly HashSet<int> apiMapDownloadBusy = new();
        private readonly ConcurrentDictionary<int, Map> maps = new();
        private readonly ConcurrentDictionary<(int, int), ContinentFloor> floors = new();
        private readonly AutoResetEvent apiMapDownloadEvent = new(false);

        private readonly System.Windows.Forms.Timer mClearStatusTimer = new();

        public ReaderForm()
        {
            this.InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.apiThread = new Thread(this.ApiLoopAsync);
            this.mumbleLoopThread = new Thread(this.MumbleLoop);
            this.mClearStatusTimer.Tick += this.ClearStatus;

            this.apiThread.Start();
            this.mumbleLoopThread.Start();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            this.UpdateStatus("Shutting down");
            this.stopRequested = true;
            this.apiMapDownloadEvent.Set();
        }

        private void UpdateStatus(string? message, TimeSpan? timeToShow = default)
        {
            this.labelStatus.Invoke(new Action<string>(m =>
            {
                this.labelStatus.Text = m;
                this.labelStatus.Visible = !string.IsNullOrWhiteSpace(m);

                if (timeToShow?.TotalMilliseconds >= 1)
                {
                    this.mClearStatusTimer.Interval = (int)timeToShow.Value.TotalMilliseconds;
                    this.mClearStatusTimer.Enabled = true;
                }
                else
                {
                    this.mClearStatusTimer.Enabled = false;
                }
            }), message);
        }

        private void ClearStatus(object? sender, EventArgs e) =>
            this.UpdateStatus(null);


        private async void ApiLoopAsync()
        {
            while (!this.stopRequested)
            {
                this.apiMapDownloadEvent.WaitOne();
                if (this.stopRequested)
                    break;

                int mapId = this.apiMapDownloadQueue.Dequeue();
                this.UpdateStatus($"Downloading API information for map {mapId}");

                var map = await this.client.WebApi.V2.Maps.GetAsync(mapId).ConfigureAwait(false);
                this.maps[mapId] = map;

                foreach (int floorId in map.Floors)
                {
                    if (!this.floors.ContainsKey((map.ContinentId, floorId)))
                    {
                        this.UpdateStatus($"Downloading API information for floor {floorId} on continent {map.ContinentId}");
                        var floor = await this.client.WebApi.V2.Continents[map.ContinentId].Floors.GetAsync(floorId).ConfigureAwait(false);
                        this.floors[(map.ContinentId, floorId)] = floor;
                    }
                }

                this.apiMapDownloadBusy.Remove(mapId);
                this.UpdateStatus($"Map download status: {map.HttpResponseInfo!.CacheState}", TimeSpan.FromSeconds(3));
            }
        }

        private void MumbleLoop()
        {
            int mapId = 0;

            do
            {
                bool shouldRun = true;
                this.client.Mumble.Update();
                if (!this.client.Mumble.IsAvailable)
                    shouldRun = false;

                int newMapId = this.client.Mumble.MapId;
                if (newMapId == 0)
                    shouldRun = false;

                if (shouldRun)
                {
                    if (newMapId != mapId && !this.apiMapDownloadBusy.Contains(mapId))
                    {
                        this.apiMapDownloadBusy.Add(newMapId);
                        this.apiMapDownloadQueue.Enqueue(newMapId);
                        this.apiMapDownloadEvent.Set();
                        mapId = newMapId;
                    }

                    try
                    {
                        this.Invoke(new Action<IGw2MumbleClient>(m =>
                        {
                            this.textBoxVersion.Text = m.Version.ToString();
                            this.textBoxTick.Text = m.Tick.ToString();
                            this.textBoxAvatarPosition1.Text = m.AvatarPosition.X.ToString();
                            this.textBoxAvatarPosition2.Text = m.AvatarPosition.Y.ToString();
                            this.textBoxAvatarPosition3.Text = m.AvatarPosition.Z.ToString();
                            this.textBoxAvatarFront1.Text = m.AvatarFront.X.ToString();
                            this.textBoxAvatarFront2.Text = m.AvatarFront.Y.ToString();
                            this.textBoxAvatarFront3.Text = m.AvatarFront.Z.ToString();
                            this.textBoxName.Text = m.Name;
                            this.textBoxCameraPosition1.Text = m.CameraPosition.X.ToString();
                            this.textBoxCameraPosition2.Text = m.CameraPosition.Y.ToString();
                            this.textBoxCameraPosition3.Text = m.CameraPosition.Z.ToString();
                            this.textBoxCameraFront1.Text = m.CameraFront.X.ToString();
                            this.textBoxCameraFront2.Text = m.CameraFront.Y.ToString();
                            this.textBoxCameraFront3.Text = m.CameraFront.Z.ToString();

                            this.textBoxRawIdentity.Text = m.RawIdentity;
                            this.textBoxCharacterName.Text = m.CharacterName;
                            this.textBoxRace.Text = m.Race.ToString();
                            this.textBoxSpecialization.Text = m.Specialization.ToString();
                            this.textBoxTeamColorId.Text = m.TeamColorId.ToString();
                            this.checkBoxCommander.Checked = m.IsCommander;
                            this.textBoxFieldOfView.Text = m.FieldOfView.ToString();
                            this.textBoxUiSize.Text = m.UiSize.ToString();

                            this.textBoxServerAddress.Text = $"{m.ServerAddress}:{m.ServerPort}";
                            this.textBoxMapId.Text = m.MapId.ToString();
                            this.textBoxMapType.Text = m.MapType.ToString();
                            this.textBoxShardId.Text = m.ShardId.ToString();
                            this.textBoxInstance.Text = m.Instance.ToString();
                            this.textBoxBuildId.Text = m.BuildId.ToString();
                            this.checkBoxUiStateMapOpen.Checked = m.IsMapOpen;
                            this.checkBoxUiStateCompassTopRight.Checked = m.IsCompassTopRight;
                            this.checkBoxUiStateCompassRotationEnabled.Checked = m.IsCompassRotationEnabled;
                            this.checkBoxUiStateGameFocus.Checked = m.DoesGameHaveFocus;
                            this.checkBoxUiStateCompetitive.Checked = m.IsCompetitiveMode;
                            this.checkBoxUiStateInputFocus.Checked = m.DoesAnyInputHaveFocus;
                            this.checkBoxUiStateInCombat.Checked = m.IsInCombat;
                            this.textBoxCompassWidth.Text = m.Compass.Width.ToString();
                            this.textBoxCompassHeight.Text = m.Compass.Height.ToString();
                            this.textBoxCompassRotation.Text = m.CompassRotation.ToString();
                            this.textBoxPlayerCoordsX.Text = m.PlayerLocationMap.X.ToString();
                            this.textBoxPlayerCoordsY.Text = m.PlayerLocationMap.Y.ToString();
                            this.textBoxMapCenterX.Text = m.MapCenter.X.ToString();
                            this.textBoxMapCenterY.Text = m.MapCenter.Y.ToString();
                            this.textBoxMapScale.Text = m.MapScale.ToString();
                            this.textBoxProcessId.Text = m.ProcessId.ToString();
                            this.textBoxMount.Text = m.Mount.ToString();

                            if (this.maps.TryGetValue(m.MapId, out var map))
                            {
                                this.textBoxMapName.Text = map.Name;

                                var mapPosition = m.AvatarPosition.ToMapCoords(CoordsUnit.Mumble);
                                this.textBoxMapPosition1.Text = mapPosition.X.ToString();
                                this.textBoxMapPosition2.Text = mapPosition.Y.ToString();
                                this.textBoxMapPosition3.Text = mapPosition.Z.ToString();

                                var continentPosition = m.AvatarPosition.ToContinentCoords(CoordsUnit.Mumble, map.MapRect, map.ContinentRect);
                                this.textBoxContinentPosition1.Text = continentPosition.X.ToString();
                                this.textBoxContinentPosition2.Text = continentPosition.Y.ToString();
                                this.textBoxContinentPosition3.Text = continentPosition.Z.ToString();

                                ContinentFloorRegionMapPoi? closestWaypoint = null;
                                Coordinates2 closestWaypointPosition = default;
                                double closestWaypointDistance = double.MaxValue;
                                ContinentFloorRegionMapPoi? closestPoi = null;
                                Coordinates2 closestPoiPosition = default;
                                double closestPoiDistance = double.MaxValue;
                                foreach (int floorId in map.Floors)
                                {
                                    if (!this.floors.TryGetValue((map.ContinentId, floorId), out var floor))
                                        continue;

                                    if (!floor.Regions.TryGetValue(map.RegionId, out var floorRegion))
                                        continue;

                                    if (!floorRegion.Maps.TryGetValue(map.Id, out var floorMap))
                                        continue;

                                    foreach (var (_, poi) in floorMap.PointsOfInterest)
                                    {
                                        double distanceX = Math.Abs(continentPosition.X - poi.Coord.X);
                                        double distanceZ = Math.Abs(continentPosition.Z - poi.Coord.Y);
                                        double distance = Math.Sqrt(Math.Pow(distanceX, 2) + Math.Pow(distanceZ, 2));
                                        switch (poi.Type.Value)
                                        {
                                            case PoiType.Waypoint when distance < closestWaypointDistance:
                                                closestWaypointPosition = poi.Coord;
                                                closestWaypointDistance = distance;
                                                closestWaypoint = poi;
                                                break;
                                            case PoiType.Landmark when distance < closestPoiDistance:
                                                closestPoiPosition = poi.Coord;
                                                closestPoiDistance = distance;
                                                closestPoi = poi;
                                                break;
                                        }
                                    }
                                }

                                if (closestWaypoint is not null)
                                {
                                    this.textBoxWaypoint.Text = closestWaypoint.Name;
                                    int poiId = closestWaypoint.Id;
                                    this.textBoxWaypointLink.Text = closestWaypoint.ChatLink;
                                    this.textBoxWaypointContinentDistance.Text = closestWaypointDistance.ToString();
                                    this.textBoxWaypointContinentPosition1.Text = closestWaypoint.Coord.X.ToString();
                                    this.textBoxWaypointContinentPosition2.Text = closestWaypoint.Coord.Y.ToString();
                                    double angle = Math.Atan2(continentPosition.Z - closestWaypointPosition.Y, continentPosition.X - closestWaypointPosition.X) * 180 / Math.PI;
                                    this.textBoxWaypointDirection1.Text = GetDirectionFromAngle(angle).ToString();
                                    this.textBoxWaypointDirection2.Text = angle.ToString();
                                }
                                else
                                {
                                    this.textBoxWaypoint.Text = string.Empty;
                                    this.textBoxWaypointLink.Text = string.Empty;
                                    this.textBoxWaypointContinentDistance.Text = string.Empty;
                                    this.textBoxWaypointContinentPosition1.Text = string.Empty;
                                    this.textBoxWaypointContinentPosition2.Text = string.Empty;
                                    this.textBoxWaypointDirection1.Text = string.Empty;
                                    this.textBoxWaypointDirection2.Text = string.Empty;
                                }

                                if (closestPoi is not null)
                                {
                                    this.textBoxPoi.Text = closestPoi.Name;
                                    int poiId = closestPoi.Id;
                                    this.textBoxPoiLink.Text = closestPoi.ChatLink;
                                    this.textBoxPoiContinentDistance.Text = closestPoiDistance.ToString();
                                    this.textBoxPoiContinentPosition1.Text = closestPoi.Coord.X.ToString();
                                    this.textBoxPoiContinentPosition2.Text = closestPoi.Coord.Y.ToString();
                                    double angle = Math.Atan2(continentPosition.Z - closestPoiPosition.Y, continentPosition.X - closestPoiPosition.X) * 180 / Math.PI;
                                    this.textBoxPoiDirection1.Text = GetDirectionFromAngle(angle).ToString();
                                    this.textBoxPoiDirection2.Text = angle.ToString();
                                }
                                else
                                {
                                    this.textBoxPoi.Text = string.Empty;
                                    this.textBoxPoiLink.Text = string.Empty;
                                    this.textBoxPoiContinentDistance.Text = string.Empty;
                                    this.textBoxPoiContinentPosition1.Text = string.Empty;
                                    this.textBoxPoiContinentPosition2.Text = string.Empty;
                                    this.textBoxPoiDirection1.Text = string.Empty;
                                    this.textBoxPoiDirection2.Text = string.Empty;
                                }
                            }
                        }), this.client.Mumble);
                    }
                    catch (ObjectDisposedException)
                    {
                        // The application is likely closing
                        break;
                    }
                }

                Thread.Sleep(1000 / 60);
            } while (!this.stopRequested);
        }

        private static Direction GetDirectionFromAngle(double angle) => angle switch
        {
            < -168.75 => Direction.West,
            < -146.25 => Direction.WestNorthWest,
            < -123.75 => Direction.NorthWest,
            < -101.25 => Direction.NorthNorthWest,
            < -78.75 => Direction.North,
            < -56.25 => Direction.NorthNorthEast,
            < -33.75 => Direction.NorthEast,
            < -11.25 => Direction.EastNorthEast,
            < 11.25 => Direction.East,
            < 33.75 => Direction.EastSouthEast,
            < 56.25 => Direction.SouthEast,
            < 78.78 => Direction.SouthSouthEast,
            < 101.25 => Direction.South,
            < 123.75 => Direction.SouthSouthWest,
            < 146.25 => Direction.SouthWest,
            < 168.75 => Direction.WestSouthWest,
            _ => Direction.West
        };

        private enum Direction
        {
            North,
            NorthNorthEast,
            NorthEast,
            EastNorthEast,
            East,
            EastSouthEast,
            SouthEast,
            SouthSouthEast,
            South,
            SouthSouthWest,
            SouthWest,
            WestSouthWest,
            West,
            WestNorthWest,
            NorthWest,
            NorthNorthWest
        }
    }
}
