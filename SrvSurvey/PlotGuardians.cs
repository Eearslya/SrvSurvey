﻿using SrvSurvey.game;
using SrvSurvey.units;
using System.Drawing.Drawing2D;
using static SrvSurvey.game.GuardianSiteData;

namespace SrvSurvey
{
    public enum Mode
    {
        siteType,   // ask about site type
        heading,    // ask about site heading
        map,        // show site map
        origin,     // show alignment to site origin
    }

    internal partial class PlotGuardians : PlotBase, IDisposable
    {
        private SiteTemplate? template;
        private Image? siteMap;
        private Image? trails;
        private Image? underlay;
        public Image? headingGuidance;
        private string highlightPoi; // tmp
        private SitePOI nearestPoi;

        /// <summary> Site map width </summary>
        private float ux;
        /// <summary> Site map height </summary>
        private float uy;

        private Mode mode;
        private PointF commanderOffset;

        private GuardianSiteData siteData { get => game?.nearBody?.siteData!; }

        private PlotGuardians() : base()
        {
            if (PlotGuardians.instance != null)
            {
                Game.log("Why are there multiple PlotGuardians?");
                PlotGuardians.instance.Close();
                Application.DoEvents();
                PlotGuardians.instance.Dispose();
            }

            PlotGuardians.instance = this;
            InitializeComponent();

            //this.Width = 500;
            //this.Height = 500;

            SiteTemplate.Import();

            this.scale = 0.55f;

            this.nextMode();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            PlotGuardians.instance = null;
        }

        public override void reposition(Rectangle gameRect)
        {
            if (gameRect == Rectangle.Empty)
            {
                this.Opacity = 0;
                return;
            }

            this.Opacity = Game.settings.Opacity;
            Elite.floatLeftMiddle(this, gameRect);
        }

        private void PlotGuardians_Load(object sender, EventArgs e)
        {
            this.initialize();
            this.reposition(Elite.getWindowRect(true));
        }

        protected override void initialize()
        {
            base.initialize();

            this.nextMode();
        }

        private void nextMode()
        {
            /*************************************************************
             * Sequence is:
             * A) Site type
             * B) Site heading
             * C) Show the map
             */

            if (siteData.type == GuardianSiteData.SiteType.unknown)
            {
                // we need to know the site type before anything else
                this.setMode(Mode.siteType);
            }
            else if (siteData.siteHeading == -1 || siteData.siteHeading == 0)
            {
                // then we need to know the site heading
                this.setMode(Mode.heading);
            }
            else
            {
                // we can show the map after that
                this.loadSiteTemplate();
                this.setMode(Mode.map);
            }
        }

        private void setMode(Mode newMode)
        {
            Game.log($"* * *> PlotGuardians: changing mode from: {this.mode} to: {newMode}");

            // do not allow some modes before we know others
            if (siteData.type == GuardianSiteData.SiteType.unknown && newMode != Mode.siteType)
            {
                Game.log($"PlotGuardians: site type must be known first");
                newMode = Mode.siteType;
            }

            if (siteData.siteHeading == -1 && (newMode != Mode.siteType && newMode != Mode.heading))
            {
                Game.log($"PlotGuardians: heading must be known first");
                newMode = Mode.heading;
            }

            this.mode = newMode;
            this.Invalidate();

            // show or hide the heading vertical stripe helper
            if (!Game.settings.disableRuinsMeasurementGrid)
            {
                if (this.mode == Mode.heading)
                {
                    PlotVertialStripe.mode = PlotVertialStripe.mode = PlotVertialStripe.Mode.Buttress;
                    Program.showPlotter<PlotVertialStripe>();
                }
                else
                    Program.closePlotter(nameof(PlotVertialStripe));
            }

            // load heading guidance image if that is the next mode
            if (this.mode == Mode.heading)
                this.loadHeadingGuidance();

            if (this.mode == Mode.origin)
            {
                if (!Game.settings.disableAerialAlignmentGrid)
                {
                    switch (this.siteData.type)
                    {
                        case SiteType.alpha:
                            PlotVertialStripe.mode = PlotVertialStripe.Mode.Alpha;
                            break;
                        case SiteType.beta:
                            PlotVertialStripe.mode = PlotVertialStripe.Mode.Beta;
                            break;
                        case SiteType.gamma:
                            PlotVertialStripe.mode = PlotVertialStripe.Mode.Gamma;
                            break;

                        // TODO: implement more for other site types

                        default:
                            return;
                    }
                    Program.showPlotter<PlotVertialStripe>();
                }
            }
            else if (this.mode != Mode.heading)
            {
                // close potential plotter
                Program.closePlotter(nameof(PlotVertialStripe));
            }
        }

        protected override void onJournalEntry(SendText entry)
        {
            base.onJournalEntry(entry);

            var msg = entry.Message.ToLowerInvariant();
            if (siteData == null)
            {
                Game.log($"Why no game.nearBody?.siteData?");
                return;
            }


            // switch to/from offset/screenshot mode
            if (msg == MsgCmd.aerial)
            {
                this.setMode(Mode.origin);
                return;
            }
            if (msg == MsgCmd.map)
            {
                this.setMode(Mode.map);
                return;
            }

            // try parsing the raw text as site type?
            GuardianSiteData.SiteType parsedType = GuardianSiteData.SiteType.unknown;
            if (siteData.type == GuardianSiteData.SiteType.unknown || this.mode == Mode.siteType)
            {
                // try matching to the enum?
                if (!Enum.TryParse<GuardianSiteData.SiteType>(msg, out parsedType))
                {
                    // try matching to other strings?
                    switch (msg)
                    {
                        case "a":
                            parsedType = GuardianSiteData.SiteType.alpha;
                            break;
                        case "b":
                            parsedType = GuardianSiteData.SiteType.beta;
                            break;
                        case "g":
                            parsedType = GuardianSiteData.SiteType.gamma;
                            break;
                    }
                }
            }
            else if (msg.StartsWith(MsgCmd.site))
            {
                // '.site <alpha>'
                Enum.TryParse<GuardianSiteData.SiteType>(msg.Substring(MsgCmd.site.Length), out parsedType);
            }

            if (parsedType != GuardianSiteData.SiteType.unknown)
            {
                Game.log($"Changing site type from: '{siteData.type}' to: '{parsedType}'");
                siteData.type = parsedType;
                siteData.Save();

                this.nextMode();
                return;
            }

            // heading stuff...

            var changeHeading = false;
            int newHeading = -1;
            // try parsing some number as the site heading
            if (this.mode == Mode.heading)
                changeHeading = int.TryParse(msg, out newHeading);

            // msg is just '.heading'
            if (msg == MsgCmd.heading)
            {
                if (this.mode == Mode.heading)
                {
                    // take the current cmdr's heading
                    newHeading = game.status.Heading;
                    changeHeading = true;
                }
                else
                {
                    // move into heading mode
                    this.setMode(Mode.heading);
                    return;
                }
            }
            else if (msg.StartsWith(MsgCmd.heading))
            {
                // try parsing a number after '.heading'
                changeHeading = int.TryParse(msg.Substring(MsgCmd.heading.Length), out newHeading);
            }

            if (changeHeading)
            {
                this.setSiteHeading(newHeading);
                return;
            }

            // set Relic Tower heading
            if (msg == MsgCmd.tower)
            {
                Game.log($"Changing Relic Tower heading from: '{siteData.relicTowerHeading}' to: '{game.status.Heading}'");
                siteData.relicTowerHeading = new Angle(game.status.Heading);
                siteData.Save();
            }
            else if (msg.StartsWith(MsgCmd.heading) && int.TryParse(msg.Substring(MsgCmd.heading.Length), out newHeading))
            {
                Game.log($"Changing Relic Tower heading from: '{siteData.relicTowerHeading}' to: '{newHeading}'");
                siteData.relicTowerHeading = newHeading;
                siteData.Save();
            }

            if (changeHeading)
            {
                this.setSiteHeading(newHeading);
                return;
            }

            // empty puddle?
            if (msg == MsgCmd.empty && this.nearestPoi != null)
            {
                siteData.poiStatus[this.nearestPoi.name] = SitePoiStatus.empty;
                siteData.Save();
            }

            // temporary stuff after here
            this.xtraCmds(msg);
        }

        private void xtraCmds(string msg)
        {
            if (this.template != null && msg.StartsWith("sf"))
            {
                // temporary
                float newScale;
                if (float.TryParse(msg.Substring(3), out newScale))
                {
                    Game.log($"scaleFactor: {this.template.scaleFactor} => {newScale}");
                    this.template.scaleFactor = newScale;
                    this.Status_StatusChanged(false);
                }
            }

            if (msg == "..")
            {
                var dist = Util.getDistance(Status.here, siteData.location, (decimal)game.nearBody!.radius);
                // get angle relative to North, then adjust by siteHeading
                Angle angle = Util.getBearing(Status.here, siteData.location) - siteData.siteHeading;
                Game.log($"!! angle: {angle}, dist: {Math.Round(dist)}");
            }

            if (msg == "ll")
            {
                Game.log("Reloading site template");
                SiteTemplate.Import(true);
                this.loadSiteTemplate();
                this.Invalidate();
            }


            if (msg.StartsWith(">>"))
            {
                this.highlightPoi = msg.Substring(2).Trim();
                Game.log($"Highlighting POI: '{this.highlightPoi}'");
                this.Invalidate();
            }
        }

        protected override void onJournalEntry(CodexEntry entry)
        {
            // exit if we have no selected POI or it isn't a relic
            if (this.nearestPoi == null || this.nearestPoi.type != POIType.relic) return;

            // add POI to confirmed
            if (!siteData.poiStatus.ContainsKey(this.nearestPoi.name))
            {
                // use combat/exploration mode to know if item is present or missing
                Game.log($"POI confirmed: '{this.nearestPoi.name}' ({this.nearestPoi.type})");
                siteData.poiStatus.Add(this.nearestPoi.name, SitePoiStatus.present);
                siteData.Save();
                this.Invalidate();
            }
        }

        private void setSiteHeading(int newHeading)
        {
            Game.log($"Changing site heading from: '{siteData.siteHeading}' to: '{newHeading}'");
            siteData.siteHeading = (int)newHeading;
            siteData.Save();

            this.nextMode();
            this.Invalidate();
        }

        private void loadSiteTemplate()
        {
            if (siteData.type == GuardianSiteData.SiteType.unknown) return;

            this.template = SiteTemplate.sites[siteData.type];

            var imageFilename = $"{siteData.type}-background.png".ToLowerInvariant();
            this.siteMap = Bitmap.FromFile(Path.Combine("images", imageFilename));

            this.trails = new Bitmap(this.siteMap.Width * 2, this.siteMap.Height * 2);

            // Temporary until trail tracking works
            using (var gg = Graphics.FromImage(this.trails))
            {
                gg.Clear(Color.FromArgb(128, Color.Navy));
                //gg.FillRectangle(Brushes.Blue, -400, -400, 800, 800);
                gg.DrawRectangle(GameColors.penGameOrange8, 0, 0, this.trails.Width, this.trails.Height);
                gg.DrawLine(GameColors.penCyan8, 0, 0, trails.Width, trails.Height);
                gg.DrawLine(GameColors.penCyan8, trails.Width, 0, 0, trails.Height);
                gg.DrawLine(GameColors.penYellow8, 0, 0, trails.Height, 0);
                //gg.FillRectangle(Brushes.Red, (this.trails.Width / 2), (this.trails.Height / 2), 40, 40);
            }

            // prepare underlay
            this.underlay = new Bitmap(this.siteMap.Width * 2, this.siteMap.Height * 2);
            this.ux = this.siteMap.Width;
            this.uy = this.siteMap.Height;

            //// prepare other stuff
            //var offset = game.nearBody!.guardianSiteLocation! - touchdownLocation.Target;
            //Game.log($"Touchdown offset: {offset}");
            //this.siteTouchdownOffset = new PointF(
            //    (float)(offset.Long * template.scaleFactor),
            //    (float)(offset.Lat * -template.scaleFactor));
            //Game.log(siteTouchdownOffset);
            //Game.log($"siteTouchdownOffset: {siteTouchdownOffset}");

            this.Status_StatusChanged(false);
        }

        private void loadHeadingGuidance()
        {
            if (this.headingGuidance != null)
            {
                this.headingGuidance.Dispose();
                this.headingGuidance = null;
            }

            var filepath = Path.Combine("images", $"{siteData.type}-heading-guide.png");
            if (File.Exists(filepath))
            {
                using (var img = Bitmap.FromFile(filepath))
                    this.headingGuidance = new Bitmap(img);
            }
        }

        protected override void Status_StatusChanged(bool blink)
        {
            base.Status_StatusChanged(blink);

            // take current heading if blink detected whilst waiting for a heading
            if (this.mode == Mode.heading && blink)
            {
                this.setSiteHeading(game.status.Heading);
            }
            else if (this.nearestPoi != null && this.mode == Mode.map && blink)
            {
                // confirm POI is missing
                var poiPresent = (game.status.Flags & StatusFlags.CargoScoopDeployed) > 0;
                var poiStatus = poiPresent ? SitePoiStatus.present : SitePoiStatus.absent;

                Game.log($"Confirming POI {poiStatus}: '{this.nearestPoi.name}' ({this.nearestPoi.type})");
                siteData.poiStatus[this.nearestPoi.name] = poiStatus;
                siteData.Save();
                this.Invalidate();
            }

            // prepare other stuff
            if (template != null && siteData != null)
            {
                //var offset = game.nearBody!.guardianSiteLocation! - Status.here;
                //this.commanderOffset = new PointF(
                //    (float)(offset.Long * template.scaleFactor),
                //    (float)(offset.Lat * -template.scaleFactor));
                //Game.log($"commanderOffset old: {commanderOffset}");
                var td = new TrackingDelta(game.nearBody!.radius, siteData.location);
                var ss = 1f;
                this.commanderOffset = new PointF(
                    (float)td.dx * ss,
                    -(float)td.dy * ss);
                //Game.log($"commanderOffset: {commanderOffset}");


                /* TODO: get trail tacking working
                using (var gg = Graphics.FromImage(this.trails!))
                {
                    gg.ResetTransform();
                    //gg.FillRectangle(Brushes.Yellow, -10, -10, 20, 20);

                    // shift by underlay size
                    //gg.TranslateTransform(ux, uy);
                    //// rotate by site heading only
                    //gg.RotateTransform(+this.siteHeading);
                    //gg.ScaleTransform(this.template!.scaleFactor, this.template!.scaleFactor);


                    // shift by underlay size and rotate by site heading
                    //gg.TranslateTransform(this.trails!.Width / 2, this.trails.Height / 2);
                    gg.TranslateTransform(this.template.imageOffset.X, this.template.imageOffset.Y);
                    gg.RotateTransform(-this.siteHeading);

                    // draw the site bitmap and trails(?)
                    const float rSize = 5;
                    float x = -commanderOffset.X;
                    float y = -commanderOffset.Y;

                    var rect = new RectangleF(
                        x, y, //x - rSize, y - rSize,
                        rSize * 2, rSize * 2
                    );
                    Game.log($"smudge: {rect}");
                    gg.FillEllipse(Brushes.Navy, rect);
                }
                // */

                /*
                using (var g = Graphics.FromImage(this.plotSmudge))
                {
                    g.TranslateTransform(this.siteTemplate.imageOffset.X, this.siteTemplate.imageOffset.Y);

                    float rotation = (float)numSiteHeading.Value;
                    rotation = rotation % 360;
                    g.RotateTransform(-rotation);

                    PointF dSrv = (PointF)(this.pointSrv - this.survey.pointSettlement);
                    g.FillEllipse(Stationary.TireTracks,
                        dSrv.X - 5, dSrv.Y - 5, //  + srvFix.Y, 
                        10, 10);
                    //g.DrawLine(Pens.Pink, dSrv.X, dSrv.Y, 0, 0);

                }
                */
            }

            this.Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            //Game.log($"-- -- --> PlotGuardians: OnPaintBackground {this.template?.imageOffset} / {this.template?.scaleFactor}");
            if (this.template != null)
            {
                // alpha
                //this.template.scaleFactor = 0.88f;
                //this.template.imageOffset.X = 737;
                //this.template.imageOffset.Y = 885;


                //this.template.scaleFactor = 0.75f;
                //this.template.imageOffset.X = 730;
                //this.template.imageOffset.Y = 885;

                // beta
                //this.template.scaleFactor = 1.15f;
                //this.template.scaleFactor = 1.3f;

                //this.template.imageOffset.X = 652;
                //this.template.imageOffset.Y = 714;

                //this.template.imageOffset.X = 638;
                //this.template.imageOffset.Y = 752;

                //this.template.imageOffset.X = 644;
                //this.template.imageOffset.Y = 795;

                //this.template.imageOffset.X = 652;
                //this.template.imageOffset.Y = 714;

                // gamma
                //this.template.scaleFactor = 1.15f;

                //this.template.imageOffset.X = 915;
                //this.template.imageOffset.Y = 685;

                /*
    "imageOffset": "638,752",
    "imageOffset": "652,714",
                */
            }

            base.OnPaintBackground(e);
            this.g = e.Graphics;
            switch (this.mode)
            {
                case Mode.siteType:
                    this.drawSiteTypeHelper();
                    return;

                case Mode.heading:
                    this.drawSiteHeadingHelper();
                    return;

                case Mode.origin:
                    this.drawTrackOrigin();
                    return;

                case Mode.map:
                    this.drawSiteMap();
                    return;
            }
        }

        private void drawTrackOrigin()
        {
            g.ResetTransform();
            this.clipToMiddle();
            g.TranslateTransform(mid.Width, mid.Height);
            g.RotateTransform(360 - siteData.siteHeading);
            var sI = 10;
            var sO = 20;
            var vr = 5;

            // calculate deltas from site origin to ship
            var td = new TrackingDelta(game.nearBody!.radius, siteData.location!);
            var x = -(float)td.dx;
            var y = (float)td.dy;

            // adjust scale depending on distance from target
            var sf = 1f;
            if (td.distance < 40)
                sf = 3f;
            else if (td.distance < 70)
                sf = 2f;
            else if (td.distance > 170)
                sf = 0.5f;
            g.ScaleTransform(sf, sf);

            var siteBrush = GameColors.brushOffTarget;
            var sitePenI = GameColors.penGameOrangeDim2;
            var sitePenO = GameColors.penGameOrangeDim2;
            var shipPen = GameColors.penGreen2;
            if (td.distance < 10)
            {
                //siteBrush = GameColors.brushOnTarget;
                sitePenI = GameColors.penGameOrange2;
                shipPen = GameColors.penCyan2;
            }
            if (td.distance < 20)
            {
                siteBrush = new HatchBrush(HatchStyle.Percent50, GameColors.OrangeDim, Color.Transparent); // GameColors.brushOnTarget;
                sitePenO = GameColors.penGameOrange2;
            }

            // draw origin marker
            g.FillEllipse(siteBrush, -sO, -sO, +sO * 2, +sO * 2);
            g.DrawEllipse(sitePenO, -sO, -sO, +sO * 2, +sO * 2);
            g.DrawEllipse(sitePenI, -sI, -sI, +sI + sI, +sI + sI);

            // draw moving ship circle marker, showing ship heading
            var rect = new RectangleF(
                    x - vr, y - vr,
                    +vr * 2, +vr * 2);
            g.DrawEllipse(shipPen, rect);
            var dHeading = new Angle(game.status.Heading);
            //var dHeading = new Angle(game.status.Heading) - siteData.siteHeading;
            //var dHeading = new Angle(siteData.siteHeading) - game.status.Heading;
            var dx = (float)Math.Sin(dHeading.radians) * 10F;
            var dy = (float)Math.Cos(dHeading.radians) * 10F;
            g.DrawLine(shipPen, x, y, x + dx, y - dy);
            this.drawCompassLines(siteData.siteHeading);

            Point[] pps = { new Point((int)x, (int)y) };
            g.TransformPoints(CoordinateSpace.Page, CoordinateSpace.World, pps);

            g.ResetTransform();
            this.clipToMiddle(4, 26, 4, 24);
            g.TranslateTransform(mid.Width, mid.Height);
            g.ScaleTransform(sf, sf);

            var pp = pps[0]; // new Point((int)pps[0].X, (int)pps[0].Y); // new Point((int)sz.Width, -(int)sz.Height);
            pp.Offset(-mid.Width, -mid.Height);

            var cs = 6; // cap size

            // vertical arrow
            if (Math.Abs(pp.Y) > 25)
            {
                var nn = pp.Y < 0 ? -1 : +1; // toggle up/down
                var z1 = new Size(0, 15 * -nn);
                var z2 = new Size(0, -pp.Y);
                var p1 = Point.Add(pp, z1);
                var p2 = Point.Add(pp, z2);

                g.DrawLine(GameColors.penCyan2, p1, p2);
                g.DrawLine(GameColors.penCyan2, p2.X, p2.Y, p2.X - cs, p2.Y + (cs * nn));
                g.DrawLine(GameColors.penCyan2, p2.X, p2.Y, p2.X + cs, p2.Y + (cs * nn));
            }

            // horizontal arrow
            if (Math.Abs(pp.X) > 25)
            {
                var nn = pp.X < 0 ? +1 : -1; ; // toggle left/right
                var z1 = new Size(15 * nn, 0);
                var z2 = new Size(-pp.X, 0);
                var p1 = Point.Add(pp, z1);
                var p2 = Point.Add(pp, z2);

                g.DrawLine(GameColors.penCyan2, p1, p2);
                g.DrawLine(GameColors.penCyan2, p2.X, p2.Y, p2.X - (cs * nn), p2.Y - cs);
                g.DrawLine(GameColors.penCyan2, p2.X, p2.Y, p2.X - (cs * nn), p2.Y + cs);
            }

            var alt = game.status.Altitude.ToString("N0");
            var targetAlt = 0d;
            switch (siteData.type)
            {
                case GuardianSiteData.SiteType.alpha:
                    targetAlt = Game.settings.aerialAltAlpha;
                    break;
                case GuardianSiteData.SiteType.beta:
                    targetAlt = Game.settings.aerialAltBeta;
                    break;
                case GuardianSiteData.SiteType.gamma:
                    targetAlt = Game.settings.aerialAltGamma;
                    break;
            }
            //var footerTxt = $"Offset: {pp.X}m, {pp.Y}m  | Alt: {alt}m | Target: {targetAlt}m";
            var footerTxt = $"Altitude: {alt}m | Target altitude: {targetAlt}m";

            // header text and rotation arrows
            this.drawOriginRotationGuide();

            // choose a font color: too low: blue, too high: red, otherwise orange
            Brush footerBrush;
            var altDiff = (int)game.status.Altitude - targetAlt;
            if (altDiff < -50)
                footerBrush = GameColors.brushCyan;
            else if (altDiff > 50)
                footerBrush = Brushes.Red;
            else
                footerBrush = GameColors.brushGameOrange;

            this.drawFooterText(footerTxt, footerBrush);
        }

        private void drawOriginRotationGuide()
        {
            g.ResetTransform();
            g.TranslateTransform(mid.Width, mid.Height);

            var adjustAngle = siteData.siteHeading - game.status.Heading;

            Brush brush = GameColors.brushGameOrange;
            if (Math.Abs(adjustAngle) > 2)
            {
                brush = GameColors.brushCyan;
                var ax = 30f;
                var ay = -mid.Height * 0.8f;
                var ad = 80;
                var ac = 10;
                if (adjustAngle < 0)
                {
                    ax *= -1;
                    ac *= -1;
                    ad *= -1;
                }
                var pp = GameColors.penCyan2;
                g.DrawLine(pp, ax, ay, ax + ad, ay);
                g.DrawLine(pp, ax + ad, ay, ax + ad - ac, ay - 10);
                g.DrawLine(pp, ax + ad, ay, ax + ad - ac, ay + 10);
            }

            var headerTxt = $"Site heading: {siteData.siteHeading}° | Rotate ship ";
            headerTxt += adjustAngle > 0 ? "right" : "left";
            headerTxt += $" {adjustAngle}°";

            this.drawHeaderText(headerTxt, brush);
        }

        private void drawSiteMap()
        {
            if (g == null) return;
            //this.drawHeaderText($"{siteData.nameLocalised} | {siteData.type} | {siteData.siteHeading}°");
            //this.drawFooterText($"{game.nearBody!.bodyName}");

            if (this.underlay == null)
            {
                Game.log("WHy no underlay?");
                return;
            }

            g.ResetTransform();
            this.clipToMiddle(4, 26, 4, 24);
            g.TranslateTransform(mid.Width, mid.Height);
            g.ScaleTransform(this.scale, this.scale);
            g.RotateTransform(-game.status.Heading);

            // prepare underlay image
            using (var gg = Graphics.FromImage(this.underlay!))
            {
                gg.ResetTransform();
                //gg.Clear(Color.Black);
                //gg.RotateTransform(360 - game.status!.Heading);

                // shift by underlay size
                gg.TranslateTransform(ux, uy);
                // rotate by site heading only
                gg.RotateTransform(+siteData.siteHeading);
                gg.ScaleTransform(this.template!.scaleFactor, this.template!.scaleFactor);

                // draw the site bitmap and trails(?)
                gg.DrawImageUnscaled(this.siteMap!, -this.template!.imageOffset.X, -this.template.imageOffset.Y);

                //var ix = -(double)this.template!.imageOffset.X * (double)this.template!.scaleFactor;
                //var iy = -(double)this.template.imageOffset.Y * (double)this.template!.scaleFactor;
                //gg.DrawImageUnscaled(this.siteMap!, (int)ix, (int)iy); // -this.template!.imageOffset.X * this.template!.scaleFactor, -this.template.imageOffset.Y * this.template!.scaleFactor);
                //int xx = 0; // -this.trails!.Width;
                //int yy = 0; // -this.trails!.Height / 2;
                //gg.DrawImageUnscaled(this.trails!, -this.template!.imageOffset.X, -this.template.imageOffset.Y);
            }

            float x = commanderOffset.X;
            float y = commanderOffset.Y;

            g.DrawImageUnscaled(this.underlay!, -(int)(ux - x), -(int)(uy - y));

            // draw compass rose lines centered on underlay
            g.DrawLine(Pens.DarkRed, -this.Width * 2, y, +this.Width * 2, y);
            g.DrawLine(Pens.DarkRed, x, y, x, +this.Height * 2);
            g.DrawLine(Pens.Red, x, -this.Height * 2, x, y);
            g.ResetClip();

            // this.drawTouchdownAndSrvLocation(true);

            this.drawArtifacts();

            this.drawCommander();

            //if (siteData.siteHeading != -1)
            //{
            //    //this.drawSiteSummaryFooter($"Site heading: {siteHeading}°");
            //    //var m = Util.getDistance(Status.here, siteData.location, (decimal)game.nearBody.radius);
            //    //var a = Util.getBearing(Status.here, siteData.location);
            //    //this.drawSiteSummaryFooter($"{Math.Round(m)}m / {Math.Round(a)}°");

            //    var ttd = new TrackingDelta(game.nearBody.radius, siteData.location);
            //    this.drawSiteSummaryFooter($"{ttd}");
            //}
        }

        private void drawArtifacts()
        {
            if (this.template == null || this.template.poi.Count == 0) return;

            // get pixel location of site origin relative to overlay window
            g.ResetTransform();
            g.TranslateTransform(mid.Width, mid.Height);
            g.ScaleTransform(this.scale, this.scale);
            g.RotateTransform(-game.status.Heading);

            PointF[] pts = { new PointF(commanderOffset.X, commanderOffset.Y) };
            g.TransformPoints(CoordinateSpace.Page, CoordinateSpace.World, pts);
            var siteOrigin = pts[0];

            // reset transform with origin at that point, scaled and rotated to match the commander
            g.ResetTransform();
            this.clipToMiddle();
            g.TranslateTransform(siteOrigin.X, siteOrigin.Y);
            g.ScaleTransform(this.scale, this.scale);
            g.RotateTransform(360 - game.status.Heading);

            var nearestDist = double.MaxValue;
            var nearestPt = PointF.Empty;
            var nearestDeg = 0f;

            int countRelics = 0, confirmedRelics = 0, countPuddles = 0, confirmedPuddles = 0;
            Angle aa = 0;
            var tt = "";
            // and draw all the POIs
            foreach (var poi in this.template.poi)
            {
                if (poi.type == POIType.relic)
                {
                    countRelics++;
                    if (siteData.poiStatus.ContainsKey(poi.name))
                        confirmedRelics++;
                }
                else
                {
                    countPuddles++;
                    if (siteData.poiStatus.ContainsKey(poi.name))
                        confirmedPuddles++;
                }

                // calculate render point for POI
                var deg = 180 - siteData.siteHeading - poi.angle;
                var pt = Util.rotateLine(
                    deg,
                    poi.dist);

                // render it
                this.drawSitePoi(poi, pt);

                // is this the closest POI?
                var x = pt.X - commanderOffset.X;
                var y = pt.Y - commanderOffset.Y;
                var d = Math.Sqrt(Math.Pow(x, 2) + Math.Pow(y, 2));

                //var foo = siteData.poiStatus.GetValueOrDefault(poi.name);
                //if (foo == SitePoiStatus.unknown)
                if (d < nearestDist)
                {
                    aa = Util.ToAngle(x, y) - siteData.siteHeading; //  - game.status.Heading;
                    if (y < 0) aa += 180;
                    tt = $"{aa} | {x}, {y}";
                    nearestDeg = deg;
                    nearestDist = d;
                    this.nearestPoi = poi;
                    nearestPt = pt;
                }
            }

            if (nearestDist > 75)
            {
                // make sure we're relatively close before selecting the item
                this.nearestPoi = null;
            }
            else
            {
                // draw highlight over closest POI
                g.DrawEllipse(GameColors.penCyan4, -nearestPt.X - 14, -nearestPt.Y - 14, 28, 28);
                var poiStatus = siteData.poiStatus.GetValueOrDefault(this.nearestPoi.name);

                var nextStatus = (game.status.Flags & StatusFlags.CargoScoopDeployed) > 0 ? SitePoiStatus.present : SitePoiStatus.absent; // ? "(set present)" : "(set absent)";
                var nextStatusDifferent = nextStatus != siteData.poiStatus.GetValueOrDefault(nearestPoi.name);
                var action = nextStatusDifferent ? $"(set {nextStatus})" : "";

                var footerBrush = poiStatus == SitePoiStatus.unknown || nextStatusDifferent ? GameColors.brushCyan : GameColors.brushGameOrange;
                this.drawFooterText($"{this.nearestPoi.type} {this.nearestPoi.name}: {poiStatus} {action}", footerBrush);
            }

            var headerBrush = confirmedRelics + confirmedPuddles < countRelics + countPuddles
                ? GameColors.brushCyan
                : GameColors.brushGameOrange;
            this.drawHeaderText($"Confirmed: {confirmedRelics}/{countRelics} relics, {confirmedPuddles}/{countPuddles} puddles", headerBrush);

            g.ResetTransform();
            //var aa = new Angle(180 - game.status.Heading - nearestDeg);
            //this.drawBearingTo(0, this.Height - 20, tt, nearestDist, aa);
        }

        private PointF drawSitePoi(SitePOI poi, PointF pt)
        {
            // skip unknown types ?
            //if (poi.type == POIType.unknown) return;

            // diameters: relics are bigger then puddles
            var d = poi.type == POIType.relic ? 16f : 10f;
            var dd = d / 2;

            var pen = this.getPoiPen(poi);

            // temporary highlight a particular POI
            var highlight = poi.name.StartsWith("?") || string.Compare(poi.name, this.highlightPoi, true) == 0;
            if (highlight)
            {
                pen = GameColors.penCyan8;
                //g.DrawLine(GameColors.penCyan2Dotted, 0, 0, -sz.Width, -sz.Height);
            }

            g.DrawEllipse(pen, -pt.X - dd, -pt.Y - dd, d, d);

            // and return distance from 
            return pt;
        }

        private Pen getPoiPen(SitePOI poi)
        {
            SitePoiStatus status = SitePoiStatus.unknown;
            if (siteData.poiStatus.ContainsKey(poi.name))
                status = siteData.poiStatus[poi.name];

            switch (poi.type)
            {
                case POIType.relic:
                    return status == SitePoiStatus.present
                        ? GameColors.penPoiRelicPresent
                        : status == SitePoiStatus.absent
                            ? GameColors.penPoiRelicMissing
                            : GameColors.penPoiRelicUnconfirmed;

                case POIType.orb:
                case POIType.casket:
                case POIType.tablet:
                case POIType.totem:
                case POIType.urn:
                    switch (status)
                    {
                        case SitePoiStatus.present: return GameColors.penPoiPuddlePresent; 
                        case SitePoiStatus.absent: return GameColors.penPoiPuddleMissing; 
                        case SitePoiStatus.unknown: return GameColors.penPoiPuddleUnconfirmed; 
                        case SitePoiStatus.empty: return GameColors.penYellow4;
                        default: return Pens.Azure;
                    }

                default:
                    return Pens.Azure;
            }
        }

        private void drawArtifactsOLD()
        {
            g.ResetTransform();
            g.TranslateTransform(mid.Width, mid.Height);
            g.ScaleTransform(this.scale, this.scale);
            g.RotateTransform(-game.status.Heading);
            float x = commanderOffset.X;
            float y = commanderOffset.Y;

            //var pp = new PointF(
            //    commanderOffset.X,// / this.scale,
            //    commanderOffset.Y// / this.scale
            //);
            PointF[] pps = { new PointF(commanderOffset.X, commanderOffset.Y) };
            g.TransformPoints(CoordinateSpace.Page, CoordinateSpace.World, pps);
            var pp = pps[0];

            //var td = new TrackingDelta(game.nearBody.radius, siteData.location);

            //pp.X *= this.scale;
            //pp.Y *= this.scale;

            //this.drawHeaderText($"?d? {pp}");

            // ** ** ** **
            g.ResetTransform();
            g.TranslateTransform(pp.X, pp.Y);
            g.ScaleTransform(this.scale, this.scale);
            g.RotateTransform(360 - game.status.Heading);

            //* BETA

            //// Synuefe TP-F b44-0 CD 1-ruins-2:
            //this.drawSitePoiOLD(151, 120, "casket");
            //this.drawSitePoiOLD(134f, 196, "tablet");
            //this.drawSitePoiOLD(122, 534, "totem");
            //this.drawSitePoiOLD(125.6f, 606, "urn");
            //this.drawSitePoiOLD(104f, 421, "tablet");
            //this.drawSitePoiOLD(90.6f, 241, "casket");
            //this.drawSitePoiOLD(141.4f, 538, "urn");
            //this.drawSitePoiOLD(111.9f, 560, "urn");
            //this.drawSitePoiOLD(26.5f, 367, "casket");
            //this.drawSitePoiOLD(5.5f, 549, "orb");
            //this.drawSitePoiOLD(357.8f, 496, "urn");
            //this.drawSitePoiOLD(286.7f, 285, "urn");
            //this.drawSitePoiOLD(258.1f, 544, "totem");
            //this.drawSitePoiOLD(193.3f, 226, "casket");
            //this.drawSitePoiOLD(181.7f, 499, "tablet");
            //this.drawSitePoiOLD(298.7f, 625, "urn");

            //// Synuefe LY-I b42-2 C 2-ruins-3:
            //this.drawSitePoiOLD(215.4f, 565, "totem");
            //this.drawSitePoiOLD(200.7f, 485, "orb");
            //this.drawSitePoiOLD(208.1f, 415, "tablet");
            //this.drawSitePoiOLD(179.3f, 394, "casket");
            //this.drawSitePoiOLD(169.2f, 276, "orb");
            //this.drawSitePoiOLD(337f, 126, "orb");
            //this.drawSitePoiOLD(300f, 183, "totem");

            //// Col 173 Sector PF-E b28-3 B 1, Ruins #2
            //this.drawSitePoiOLD(214.7f, 258, "totem");
            //this.drawSitePoiOLD(230.8f, 506, "casket");
            //this.drawSitePoiOLD(193.7f, 617, "urn");
            //this.drawSitePoiOLD(276.8f, 436, "urn");


            // relics ...

            //// Synuefe TP-F b44-0 CD 1-ruins-2:
            //this.drawSitePoiOLD(80.8f, 408, "relic");
            //this.drawSitePoiOLD(236.8f, 612, "relic"); // leaning
            //this.drawSitePoiOLD(151, 358, "relic");
            //this.drawSitePoiOLD(35.5f, 515, "relic");
            //this.drawSitePoiOLD(292.3f, 413, "relic"); // leaning
            //// this.drawSitePoi(292, 378, "relic"); // ? ish

            //// Synuefe LY-I b42-2 C 2-ruins-3:
            //this.drawSitePoiOLD(196.3f, 378, "relic");
            //this.drawSitePoiOLD(328.8f, 358, "relic");

            //// Col 173 Sector PF-E b28-3 B 1, Ruins #2
            //this.drawSitePoiOLD(253.2f, 390, "relic");

            // */

            /* ALPHA 

            // relics ...
            this.scale = 2f;
            // 
            this.drawSitePoi(32f, 80, "relic");
            this.drawSitePoi(160f, 250, "relic");
            this.drawSitePoi(176f, 147, "relic");
            this.drawSitePoi(351.6f, 502, "relic");

            // */



            // -- -- -- --
            //this.drawFooterText($"?c? {(int)pp.X},{(int)pp.Y}");
            //this.drawHeaderText($"?d? {commanderOffset}");

        }

        private void drawSitePoiOLD(float angle, float dist, string type)
        {
            var d = type == "relic" ? 16f : 10f;
            var dd = d / 2;

            var p = type == "relic"
                ? new Pen(Color.CornflowerBlue, 4) { DashStyle = DashStyle.Dash, }
                : new Pen(Color.PeachPuff, 2) { DashStyle = DashStyle.Dot, };

            var sz = Util.rotateLine(
                180 - siteData.siteHeading - angle,
                dist);
            g.DrawEllipse(p, -sz.X - dd, -sz.Y - dd, d, d);
        }

        private void drawSiteSummaryFooter(string msg)
        {
            if (g == null) return;

            // draw heading text (center bottom)
            g.ResetTransform();
            g.ResetClip();
            var sz = g.MeasureString(msg, Game.settings.fontSmall);
            var tx = mid.Width - (sz.Width / 2);
            var ty = this.Height - sz.Height - 6;
            g.DrawString(msg, Game.settings.fontSmall, GameColors.brushGameOrange, tx, ty);
        }

        private void drawSiteTypeHelper()
        {
            if (g == null) return;

            this.drawHeaderText($"{siteData.nameLocalised} | {siteData.type} | ???°");
            this.drawFooterText($"{game.nearBody!.bodyName}");
            g.ResetTransform();
            this.clipToMiddle();

            string msg;
            SizeF sz;
            var tx = 10f;
            var ty = 20f;

            this.drawFooterText($"{game.nearBody!.bodyName}");

            // if we don't know the site type yet ...
            msg = $"Need site type\r\n\r\nSend message:\r\n\r\n 'a' for Alpha\r\n\r\n 'b' for Beta\r\n\r\n 'g' for Gamma";
            sz = g.MeasureString(msg, Game.settings.font1, this.Width);
            g.DrawString(msg, Game.settings.font1, GameColors.brushCyan, tx, ty, StringFormat.GenericTypographic);
        }

        private void drawSiteHeadingHelper()
        {
            if (g == null) return;

            this.drawHeaderText($"{siteData.nameLocalised} | {siteData.type} | ???°");
            this.drawFooterText($"{game.nearBody!.bodyName}");
            g.ResetTransform();
            this.clipToMiddle();

            string msg;
            var tx = 10f;
            var ty = 20f;


            var isRuins = siteData.isRuins;
            msg = $"Need site heading\r\n\r\n■ To use current heading either:\r\n    - Toggle Cargo scoop twice\r\n    - Send message:   .heading\r\n\r\n■ Or send message: <degrees>";
            if (isRuins)
                msg += $"\r\n\r\nAlign with this buttress:";
            var sz = g.MeasureString(msg, Game.settings.fontMiddle, this.Width);

            g.DrawString(msg, Game.settings.fontMiddle, GameColors.brushCyan, tx, ty, StringFormat.GenericTypographic);

            // show location of helpful buttress
            if (isRuins && this.headingGuidance != null)
                g.DrawImage(this.headingGuidance, 40, 20 + sz.Height); //, 200, 200);
        }

        #region static accessing stuff

        private static PlotGuardians? instance;

        public static void switchMode(Mode newMode)
        {
            if (PlotGuardians.instance != null)
            {
                PlotGuardians.instance.setMode(newMode);
                Elite.setFocusED();
            }
        }

        #endregion
    }
}
