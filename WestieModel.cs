using System;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows;
using System.Windows.Media.Imaging;

namespace WestiePet
{
    // Articulated Westie and Corgi profiles share the same interaction rig.
    public sealed class WestieModel
    {
        public readonly Model3DGroup Scene = new Model3DGroup();
        public readonly AxisAngleRotation3D Heading = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 52);
        readonly Model3DGroup dog = new Model3DGroup();
        readonly TranslateTransform3D lift = new TranslateTransform3D();
        readonly SculptedCoat sculpt;
        readonly TennisToy playToy;
        readonly Model3DGroup biscuit = new Model3DGroup();
        readonly ScaleTransform3D biscuitSize = new ScaleTransform3D(0, 0, 0);
        readonly TranslateTransform3D biscuitPosition = new TranslateTransform3D();
        readonly ScaleTransform3D[] crumbSizes = new ScaleTransform3D[5];
        readonly TranslateTransform3D[] crumbPositions = new TranslateTransform3D[5];
        readonly Transform3DGroup headSkinTransform = new Transform3DGroup();
        readonly Transform3DGroup tailSkinTransform = new Transform3DGroup();
        readonly TranslateTransform3D headRest = new TranslateTransform3D();
        readonly AxisAngleRotation3D headTilt = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
        readonly AxisAngleRotation3D headNod = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
        readonly AxisAngleRotation3D headYaw = new AxisAngleRotation3D(new Vector3D(0, 1, 0), -19);
        readonly AxisAngleRotation3D tailWag = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
        readonly AxisAngleRotation3D tailRest = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
        readonly AxisAngleRotation3D[] earTurns = new AxisAngleRotation3D[2];
        readonly AxisAngleRotation3D[] legs = new AxisAngleRotation3D[4];
        readonly double[] legAngles = new double[4];
        readonly ScaleTransform3D[] eyes = new ScaleTransform3D[2];
        readonly bool naturalLook;
        readonly bool corgi;
        readonly CorgiSprite artwork;
        readonly Model3DGroup artworkTreat = new Model3DGroup();
        readonly ScaleTransform3D artworkTreatSize = new ScaleTransform3D(0, 0, 0);
        readonly TranslateTransform3D artworkTreatPosition = new TranslateTransform3D();
        public string Breed { get; private set; }
        public bool IsPreRendered { get { return artwork != null; } }
        readonly ScaleTransform3D[] closedEyes = new ScaleTransform3D[2];
        readonly ScaleTransform3D closedMouth = new ScaleTransform3D(.001, .001, .001);
        readonly TranslateTransform3D[] eyeRetraction = new TranslateTransform3D[2];
        readonly ScaleTransform3D[] pupils = new ScaleTransform3D[2];
        readonly AxisAngleRotation3D[] eyeExpression = new AxisAngleRotation3D[2];
        readonly TranslateTransform3D[] gaze = new TranslateTransform3D[2];
        readonly ScaleTransform3D noseBreath = new ScaleTransform3D(1, 1, 1, 0, -.055, .550);
        readonly TranslateTransform3D noseMotion = new TranslateTransform3D();
        double expression, sniff, jawOpen = .12, mouthRest, sleepPose;
        double lookX, lookY, headLookX, headLookY;
        double attentionX, attentionY, attentionStrength, attention, curious;
        double previousTime = double.NaN;
        double nextGlance = 1.9, glanceX, glanceY = .003, glanceStarted = -10;
        double nextBlink = 3.4, blinkStarted = -10, blinkDuration = .23;
        double nextSniff = 6.1, sniffStarted = -10, idleWagStarted = -10;
        double earListenStarted = -10, earListenSide = 1;
        double playPawStart, previousStateTime;
        string previousState;
        string pendingAction;
        bool playRecovering;
        double playActionStart;
        readonly Random behaviorRandom = new Random(113);
        readonly Random random = new Random(27);
        readonly Material white = Matte(237, 235, 229);
        readonly Material cream = Matte(233, 231, 225);
        readonly Material bright = Matte(244, 242, 235);
        readonly Material cool = Matte(235, 235, 230);
        readonly Material dark = Shiny(22, 25, 28);
        readonly Material mouth = Matte(40, 32, 33);
        readonly Material iris = Shiny(75, 58, 43);
        readonly Material sclera = Matte(246, 244, 235);
        static readonly MeshGeometry3D sphere = Sphere(24, 16);
        static readonly MeshGeometry3D treatSphere = Sphere(12, 8);
        public int TriangleCount { get; private set; }
        public bool WalkingPaused { get; set; }
        internal double PlayActionTime { get; private set; }
        internal bool PlayReady { get; private set; }
        internal Rect3D DogBounds { get { return artwork != null ? artwork.Bounds : dog.Bounds; } }
        internal bool ArtworkHitTest(double x, double y) { return artwork != null && artwork.HitTest(x, y); }

        public WestieModel() : this(false, true, "westie") { }

        public WestieModel(string breed) : this(false, true, breed) { }

        internal WestieModel(bool naturalLook, bool withCoat) : this(naturalLook, withCoat, "westie") { }

        internal WestieModel(bool naturalLook, bool withCoat, string breed)
        {
            this.naturalLook = naturalLook;
            Breed = PetSettings.ReadBreed(breed); corgi = Breed == "corgi";
            if (corgi)
            {
                // The quality-first option uses the user's high-resolution
                // artwork, with local 2.5D rigging; it is not a turntable model.
                artwork = new CorgiSprite(); Scene.Children.Add(artwork.Model);
                TriangleCount = artwork.TriangleCount;
                Scene.Children.Add(new AmbientLight(Color.FromRgb(172, 175, 180)));
                Scene.Children.Add(new DirectionalLight(Color.FromRgb(120, 113, 100), new Vector3D(-2, -3, -4)));
                playToy = new TennisToy(); Scene.Children.Add(playToy.Model); TriangleCount += playToy.TriangleCount;
                Transform3DGroup food = new Transform3DGroup(); food.Children.Add(artworkTreatSize);
                food.Children.Add(artworkTreatPosition); artworkTreat.Transform = food;
                Material baked = Matte(198, 130, 56);
                Ball(artworkTreat, 0, 0, 0, .07, .034, .025, baked, true);
                for (int s = -1; s <= 1; s += 2) for (int t = -1; t <= 1; t += 2)
                    Ball(artworkTreat, s * .061, t * .022, 0, .032, .027, .025, baked, true);
                Scene.Children.Add(artworkTreat);
                artwork.Update(0, "idle", 0, Heading.Angle, false);
                return;
            }
            // Keep the visual baseline chosen by the user; animation and
            // input fixes remain independent of these appearance settings.
            Scene.Children.Add(new AmbientLight(corgi ? Color.FromRgb(103, 111, 122) : Color.FromRgb(145, 150, 158)));
            Scene.Children.Add(new DirectionalLight(corgi ? Color.FromRgb(176, 160, 139) : Color.FromRgb(128, 123, 116), new Vector3D(-2, -3, -4)));
            Scene.Children.Add(new DirectionalLight(corgi ? Color.FromRgb(50, 66, 88) : Color.FromRgb(78, 91, 108), new Vector3D(3, -1, 1)));
            Scene.Children.Add(new DirectionalLight(corgi ? Color.FromRgb(85, 97, 114) : Color.FromRgb(105, 111, 113), new Vector3D(-1, -1, 4)));
            Scene.Children.Add(dog);
            Transform3DGroup transform = new Transform3DGroup();
            // Breed proportions apply equally to the articulated skin, face and toys.
            // The original terrier never receives this transform.
            if (corgi) transform.Children.Add(new ScaleTransform3D(1.08, .90, 1.04));
            transform.Children.Add(new RotateTransform3D(Heading));
            transform.Children.Add(lift);
            dog.Transform = transform;

            sculpt = new SculptedCoat(white, mouth, new Material[] { white, bright, cool }, withCoat, corgi);
            dog.Children.Add(sculpt.Model); TriangleCount += sculpt.TriangleCount;
            for (int i = 0; i < 4; i++)
                legs[i] = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);

            tailSkinTransform.Children.Add(new TranslateTransform3D(0, -1.00, 1.065));
            tailSkinTransform.Children.Add(new RotateTransform3D(tailWag));
            tailSkinTransform.Children.Add(new RotateTransform3D(tailRest));
            tailSkinTransform.Children.Add(new TranslateTransform3D(0, 1.00, -1.065));

            Model3DGroup head = new Model3DGroup();
            headSkinTransform.Children.Add(new TranslateTransform3D(0, -1.08, -.25));
            headSkinTransform.Children.Add(new RotateTransform3D(headNod));
            headSkinTransform.Children.Add(new RotateTransform3D(headTilt));
            headSkinTransform.Children.Add(new RotateTransform3D(headYaw));
            headSkinTransform.Children.Add(new TranslateTransform3D(0, 1.08, .25));
            headSkinTransform.Children.Add(headRest);
            Transform3DGroup ht = new Transform3DGroup();
            if (corgi) ht.Children.Add(new ScaleTransform3D(SculptedCoat.CorgiHeadWidth,
                SculptedCoat.CorgiHeadHeight, SculptedCoat.CorgiHeadDepth));
            ht.Children.Add(new TranslateTransform3D(0, corgi ? SculptedCoat.CorgiHeadY : 1.345, .45));
            ht.Children.Add(headSkinTransform);
            head.Transform = ht;
            dog.Children.Add(head);

            Ear(head, -.235, .355, -.045, -1);
            Ear(head, .235, .355, -.045, 1);

            for (int i = 0; i < 2; i++)
            {
                double x = i == 0 ? -.125 : .125;
                Model3DGroup eye = new Model3DGroup();
                Transform3DGroup et = new Transform3DGroup();
                eyes[i] = new ScaleTransform3D(1, 1, 1);
                // Eyelid openness is separate from the eyeball transform, so
                // closing the lids never flattens the iris or its highlights.
                eyeExpression[i] = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
                et.Children.Add(new RotateTransform3D(eyeExpression[i]));
                eyeRetraction[i] = new TranslateTransform3D(); et.Children.Add(eyeRetraction[i]);
                et.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), i == 0 ? -8 : 8)));
                et.Children.Add(new TranslateTransform3D(x, .170, corgi ? .320 : .345));
                eye.Transform = et;
                head.Children.Add(eye);
                // Restore the old eye-white/iris balance from the user's reference.
                double slant = i == 0 ? .006 : -.006;
                EyeSurface(eye, corgi ? .075 : .093, corgi ? .082 : .097, corgi ? .069 : .083,
                    .021, .029, slant, false, corgi ? Shiny(68, 45, 27) : sclera);
                Point3D[] lid = new Point3D[10];
                for (int k = 0; k < lid.Length; k++)
                {
                    double a = k * Math.PI / (lid.Length - 1);
                    lid[k] = new Point3D(Math.Cos(a) * (corgi ? .076 : .093),
                        Math.Sin(a) * (corgi ? .083 : .097) + slant * Math.Cos(a), .025);
                }
                Stroke(eye, lid, corgi ? .0025 : .0034, Matte(83, 73, 61));
                Point3D[] lowerLid = new Point3D[10];
                for (int k = 0; k < lowerLid.Length; k++)
                {
                    double a = Math.PI + k * Math.PI / (lowerLid.Length - 1);
                    lowerLid[k] = new Point3D(Math.Cos(a) * (corgi ? .076 : .093),
                        Math.Sin(a) * (corgi ? .070 : .083) + slant * Math.Cos(a), .025);
                }
                Stroke(eye, lowerLid, .0019, corgi ? Matte(149, 99, 51) : Matte(155, 147, 132));
                Model3DGroup pupil = new Model3DGroup();
                gaze[i] = new TranslateTransform3D();
                pupils[i] = new ScaleTransform3D(1, 1, 1);
                Transform3DGroup pupilTransform = new Transform3DGroup();
                pupilTransform.Children.Add(pupils[i]); pupilTransform.Children.Add(gaze[i]);
                pupil.Transform = pupilTransform; eye.Children.Add(pupil);
                double irisWidth = corgi ? .066 : naturalLook ? .055 : .057;
                EyeSurface(pupil, irisWidth, corgi ? .073 : .068, corgi ? .062 : .059, .052, .006, 0, false, iris);
                EyeSurface(pupil, corgi ? .049 : naturalLook ? .045 : .047, corgi ? .058 : .056,
                    corgi ? .050 : .049, .060, .004, 0, false, dark);
                Ball(pupil, -.020, .022, .067, corgi ? .010 : .012, corgi ? .012 : .014, .004, Matte(255, 255, 253));
                Ball(pupil, .020, -.028, .066, .005, .006, .0025, bright);

                Model3DGroup closed = new Model3DGroup();
                closedEyes[i] = new ScaleTransform3D(.001, .001, .001);
                Transform3DGroup ct = new Transform3DGroup(); ct.Children.Add(closedEyes[i]);
                ct.Children.Add(new TranslateTransform3D(x, .170, .345)); closed.Transform = ct;
                Point3D[] shutLid = new Point3D[13];
                for (int k = 0; k < shutLid.Length; k++)
                {
                    double lx = -.066 + k * .132 / (shutLid.Length - 1), r = lx / .066;
                    Point3D p = sculpt.ClosedFacePoint(new Point3D(x + lx, 1.508 - .012 * (1 - r * r), .82), .002);
                    shutLid[k] = p - new Vector3D(x, 1.515, .795);
                }
                Stroke(closed, shutLid, .0027, Matte(99, 89, 76)); head.Children.Add(closed);
            }
            Model3DGroup restingLips = new Model3DGroup();
            Transform3DGroup lipTransform = new Transform3DGroup();
            lipTransform.Children.Add(closedMouth);
            lipTransform.Children.Add(new TranslateTransform3D(0, -.230, .500));
            restingLips.Transform = lipTransform;
            Point3D[] lipLine = new Point3D[15];
            for (int k = 0; k < lipLine.Length; k++)
            {
                double x = -.075 + k * .150 / (lipLine.Length - 1);
                Point3D p = sculpt.ClosedFacePoint(new Point3D(x, 1.115 + .35 * x * x, .98), .0015);
                lipLine[k] = p - new Vector3D(0, 1.115, .950);
            }
            Stroke(restingLips, lipLine, .0019, Matte(112, 102, 91)); head.Children.Add(restingLips);
            // There are deliberately no separate eyebrow volumes. The forehead
            // coat flows continuously down from the ear roots to the upper lids.
            // Mouth lining is part of the carved mesh; there is no floating mouth oval.
            Model3DGroup nose = new Model3DGroup();
            Transform3DGroup noseTransform = new Transform3DGroup();
            noseTransform.Children.Add(noseBreath); noseTransform.Children.Add(noseMotion);
            nose.Transform = noseTransform; Nose(nose); head.Children.Add(nose);
            BuildTreat(head);
            playToy = new TennisToy(); dog.Children.Add(playToy.Model); TriangleCount += playToy.TriangleCount;
        }

        void BuildTreat(Model3DGroup head)
        {
            // The treat shares the muzzle transform, so it stays at the mouth
            // while the dog lowers its head and chews, including in the home preview.
            Transform3DGroup transform = new Transform3DGroup();
            transform.Children.Add(biscuitSize); transform.Children.Add(biscuitPosition);
            biscuit.Transform = transform; head.Children.Add(biscuit);
            Material baked = Matte(191, 121, 48), edge = Matte(219, 159, 79), holes = Matte(132, 80, 34);
            Ball(biscuit, 0, 0, 0, .105, .043, .035, baked, true);
            for (int side = -1; side <= 1; side += 2)
                for (int tip = -1; tip <= 1; tip += 2)
                    Ball(biscuit, side * .091, tip * .030, 0, .042, .038, .036, edge, true);
            for (int i = -1; i <= 1; i++) Ball(biscuit, i * .044, 0, .034, .006, .006, .002, holes, true);
            for (int i = 0; i < crumbSizes.Length; i++)
            {
                Model3DGroup crumb = new Model3DGroup();
                crumbSizes[i] = new ScaleTransform3D(0, 0, 0); crumbPositions[i] = new TranslateTransform3D();
                Transform3DGroup ct = new Transform3DGroup();
                ct.Children.Add(crumbSizes[i]); ct.Children.Add(crumbPositions[i]); crumb.Transform = ct;
                Ball(crumb, 0, 0, 0, .013, .010, .011, edge, true); head.Children.Add(crumb);
            }
        }

        void AnimateTreat(bool feeding, double stateTime)
        {
            double approach = SmoothStep(stateTime / .85);
            double eaten = stateTime < 1.25 ? 1 : stateTime < 2.0 ? .76 : stateTime < 2.75 ? .50 : stateTime < 3.5 ? .26 : 0;
            double size = feeding ? SmoothStep(stateTime / .18) * eaten : 0;
            biscuitSize.ScaleX = biscuitSize.ScaleY = biscuitSize.ScaleZ = size;
            biscuitPosition.OffsetX = .08 * (1 - approach);
            biscuitPosition.OffsetY = -.34 + .11 * approach;
            biscuitPosition.OffsetZ = 1.10 - .54 * approach;
            for (int i = 0; i < crumbSizes.Length; i++)
            {
                double age = stateTime - 1.25;
                if (age >= 0) age %= .75;
                bool visible = feeding && stateTime >= 1.25 && stateTime < 4 && age < .45;
                double scale = visible ? 1 - age / .45 : 0;
                crumbSizes[i].ScaleX = crumbSizes[i].ScaleY = crumbSizes[i].ScaleZ = scale;
                crumbPositions[i].OffsetX = (i - 2) * (.019 + Math.Max(0, age) * .11);
                crumbPositions[i].OffsetY = -.23 - Math.Max(0, age) * Math.Max(0, age) * .9;
                crumbPositions[i].OffsetZ = .59 + i * .012;
            }
        }

        static double SmoothStep(double value)
        {
            double t = Math.Max(0, Math.Min(1, value));
            return t * t * (3 - 2 * t);
        }

        public void LookAt(double x, double y, double strength)
        {
            attentionX = Math.Max(-1, Math.Min(1, x)); attentionY = Math.Max(-1, Math.Min(1, y));
            attentionStrength = Math.Max(0, Math.Min(1, strength));
        }

        // UI actions explicitly restart a performance even if two presses fall
        // between animation ticks. Animate also accepts standalone QA callers.
        internal void BeginAction(string state)
        {
            pendingAction = state;
            if (state == "play") { PlayActionTime = 0; PlayReady = false; }
        }

        public void Animate(double time, string state, double stateTime)
        {
            if (artwork != null)
            {
                artwork.Update(time, state, stateTime, Heading.Angle, WalkingPaused);
                Point3D mouthPoint = artwork.Mouth;
                bool feedingArtwork = state == "feed" && stateTime >= .4 && stateTime < 4.1;
                double foodAmount = !feedingArtwork ? 0 : stateTime < 1.5 ? 1 : stateTime < 2.3 ? .72 : stateTime < 3.1 ? .43 : .18;
                artworkTreatSize.ScaleX = artworkTreatSize.ScaleY = artworkTreatSize.ScaleZ = foodAmount * .72;
                double approach = Math.Max(0, 1 - (stateTime - .4) / .65);
                artworkTreatPosition.OffsetX = mouthPoint.X + (Heading.Angle < 0 ? -.18 : .18) * approach;
                artworkTreatPosition.OffsetY = mouthPoint.Y + .035 - approach * .14;
                artworkTreatPosition.OffsetZ = mouthPoint.Z + .18;
                PlayReady = state == "play"; PlayActionTime = Math.Max(0, stateTime);
                Point3D paw = artwork.FrontPaw;
                Vector3D ball = PlayBallMotion(stateTime);
                ball.X = paw.X + (Heading.Angle < 0 ? -1 : 1) * (.16 + Math.Max(0, stateTime - .72) * .10);
                ball.Y = .14 + Math.Max(0, Math.Sin((stateTime - .72) * 4)) * .045 * Math.Exp(-Math.Max(0, stateTime - .72));
                ball.Z = .18;
                playToy.Update(state == "play", ball);
                return;
            }
            bool walking = state == "walk", sleeping = state == "sleep", playing = state == "play";
            bool petting = state == "pet", feeding = state == "feed";
            bool restarting = pendingAction == state || previousState != state || stateTime < previousStateTime;
            if (playing && restarting)
            {
                playRecovering = sleepPose > .02;
                playPawStart = playRecovering ? 0 : legs[1].Angle;
                playActionStart = 0;
            }
            if (pendingAction == state) pendingAction = null;
            previousState = state; previousStateTime = stateTime;
            bool moving = walking && !WalkingPaused;
            bool happy = state == "pet" || state == "feed" || state == "play";
            double dt = double.IsNaN(previousTime) || time <= previousTime ? .042 : Math.Min(.10, time - previousTime);
            if (double.IsNaN(previousTime) || time < previousTime)
            {
                nextGlance = time + 1.9; nextBlink = time + 3.4; nextSniff = time + 6.1;
                glanceStarted = blinkStarted = sniffStarted = idleWagStarted = earListenStarted = time - 10;
            }
            previousTime = time;
            double ease = 1 - Math.Exp(-dt * 9);
            sleepPose += ((sleeping ? 1 : 0) - sleepPose) * (1 - Math.Exp(-dt * 4.6));
            if (playing && playRecovering && stateTime >= 1.2 && sleepPose < .006
                && Math.Abs(legs[2].Angle) < .65 && Math.Abs(legs[3].Angle) < .65 && Math.Abs(lift.OffsetY) < .005)
            {
                playRecovering = false; playActionStart = stateTime; playPawStart = legs[1].Angle;
            }
            PlayReady = playing && !playRecovering;
            PlayActionTime = PlayReady ? Math.Max(0, stateTime - playActionStart) : 0;
            double playTime = PlayActionTime;
            expression += ((happy ? 1 : 0) - expression) * ease;
            attention += (((sleeping || walking || playing || feeding || petting) ? 0 : attentionStrength) - attention) * (1 - Math.Exp(-dt * 5));
            double petPose = petting ? SmoothStep(stateTime / .30) * SmoothStep((3.6 - stateTime) / .55) : 0;
            double feedPose = feeding ? SmoothStep(stateTime / .6) * SmoothStep((4.5 - stateTime) / .65) : 0;
            double nuzzle = Math.Sin(stateTime * 4.5);
            double chew = feeding && stateTime > .85 && stateTime < 3.95 ? .5 + .5 * Math.Sin((stateTime - .85) * Math.PI * 2 / .75) : 0;
            AnimateTreat(feeding, stateTime);
            Vector3D playBall = PlayBallMotion(playTime);
            // Folded feet sweep through the eventual ball location while
            // standing up. Introduce the toy only after the body has settled.
            playToy.Update(PlayReady, playBall);
            double curiosityTarget = sleeping ? 0 : (state == "play" ? .85 : .24 + attention * .55);
            curious += (curiosityTarget - curious) * ease;
            // Brief observations are separated by genuine holds. The seeded
            // scheduler varies the timing without injecting per-frame noise.
            if (!sleeping && time >= nextGlance)
            {
                glanceX = (behaviorRandom.NextDouble() - .5) * .032;
                glanceY = (behaviorRandom.NextDouble() - .35) * .009;
                glanceStarted = time; nextGlance = time + 2.8 + behaviorRandom.NextDouble() * 3.4;
                earListenStarted = time + .12; earListenSide = glanceX < 0 ? -1 : 1;
                idleWagStarted = time + .55;
            }
            if (!sleeping && time >= nextBlink)
            {
                blinkStarted = time; blinkDuration = .20 + behaviorRandom.NextDouble() * .065;
                nextBlink = time + 2.8 + behaviorRandom.NextDouble() * 4.0;
            }
            if (!sleeping && time >= nextSniff)
            {
                sniffStarted = time; nextSniff = time + 7.0 + behaviorRandom.NextDouble() * 6.0;
            }
            double glanceHold = 1 - SmoothStep((time - glanceStarted - 1.1) / .6);
            double aimX = sleeping ? 0 : happy ? -.003 : walking ? -.005 : glanceX * glanceHold;
            double aimY = sleeping ? 0 : happy ? .005 : glanceY * glanceHold;
            aimX = aimX * (1 - attention) + attentionX * (naturalLook ? .018 : .023) * attention;
            aimY = aimY * (1 - attention) + (.004 + attentionY * .012) * attention;
            if (playing) { aimX = playBall.X * .021; aimY = -.011 + playBall.Y * .003; }
            double eyeEase = 1 - Math.Exp(-dt * 13);
            lookX += (aimX - lookX) * eyeEase; lookY += (aimY - lookY) * eyeEase;
            // Eye glances do not move the skull. Cursor-driven head turns have
            // a dead zone and an independent, slower target filter.
            double headEase = 1 - Math.Exp(-dt * 2);
            headLookX += ((playing ? playBall.X : HeadTarget(attentionX) * attention) - headLookX) * headEase;
            headLookY += ((playing ? -.5 : HeadTarget(attentionY) * attention) - headLookY) * headEase;
            double sniffAge = time - sniffStarted;
            double sniffTarget = !sleeping && !playing && sniffAge > 0 && sniffAge < .65
                ? Math.Sin(sniffAge / .65 * Math.PI) * (.5 + .5 * Math.Sin(sniffAge * 27)) : 0;
            sniff += (sniffTarget - sniff) * (1 - Math.Exp(-dt * 20));
            double playPrepare = PlayReady ? SmoothStep(playTime / .28) * (1 - SmoothStep((playTime - .78) / .5)) : 0;
            double bounce = moving ? (1 - Math.Cos(time * 16)) * .006 : Math.Sin(time * 2.2) * .001;
            bounce -= playPrepare * .003;
            lift.OffsetY += (((corgi ? -.29 : -.36) * sleepPose + (1 - sleepPose) * bounce) - lift.OffsetY) * ease;
            // A small lean prepares the gesture while the rear feet stay down.
            headRest.OffsetX += ((petPose * .045 * nuzzle - playPrepare * .012) - headRest.OffsetX) * ease;
            headRest.OffsetY += ((-.43 * sleepPose + .035 * petPose - .075 * feedPose - playPrepare * .012) - headRest.OffsetY) * ease;
            headRest.OffsetZ += ((.060 * sleepPose + .045 * petPose + .065 * feedPose + playPrepare * .018) - headRest.OffsetZ) * ease;
            double breath = Math.Sin(time * (sleeping ? 1.7 : 2.2));
            double tiltTarget = sleeping ? -4 : petting ? petPose * (-7 + nuzzle * 11) : -headLookX * .75;
            double nodTarget = feeding ? feedPose * (23 + chew * 5)
                : petting ? petPose * (-10 + Math.Cos(stateTime * 4.5) * 4)
                : sleeping ? 8 : playing ? 5 + playPrepare * 3 - playBall.Y * .8 : -1.5 + breath * .20 - headLookY * .7;
            double aimYaw = walking ? (Heading.Angle < 0 ? 12 : -12) : -19 + (sleeping ? -3 : headLookX * (playing ? 7 : 2.5));
            if (petting) aimYaw += petPose * nuzzle * 9;
            headTilt.Angle = MoveHeadAngle(headTilt.Angle, tiltTarget, dt, petting ? 60 : 9);
            headNod.Angle = MoveHeadAngle(headNod.Angle, nodTarget, dt, feeding || petting ? 52 : 14);
            headYaw.Angle = MoveHeadAngle(headYaw.Angle, aimYaw, dt, 12);
            double wagSpeed = playing ? 8 : happy ? 13 : walking ? 8 : 6;
            double wagTime = petting || feeding ? stateTime : time;
            double wag = Math.Sin(wagTime * wagSpeed) + Math.Sin(wagTime * wagSpeed * 2 + .4) * .14;
            double wagAge = time - idleWagStarted;
            double wagBurst = wagAge > 0 && wagAge < 1.5 ? Math.Sin(wagAge / 1.5 * Math.PI) : 0;
            double wagAmount = petting ? 32 : playing ? 11 : happy ? 21 : walking ? 7 : (wagBurst * 8 + attention * 3);
            tailWag.Angle += ((sleeping ? 54 : wag * wagAmount) - tailWag.Angle) * ease;
            tailRest.Angle = -65 * sleepPose;
            for (int i = 0; i < legs.Length; i++)
            {
                double pawTap = PlayReady && i == 1 ? PlayPawGesture(playTime) : 0;
                double stride = Math.Sin(time * 8 + (i == 0 || i == 3 ? 0 : Math.PI)) * (i < 2 ? 18 : 16);
                legs[i].Angle += (((1 - sleepPose) * (moving ? stride : pawTap)
                    + sleepPose * -70) - legs[i].Angle) * ease;
                if (PlayReady && i == 1 && sleepPose < .005)
                {
                    // The same clock controls actual paw contact and release;
                    // filtering this curve separately would make the ball lead.
                    legs[i].Angle = playTime < .22
                        ? playPawStart * (1 - SmoothStep(playTime / .22)) : pawTap;
                }
            }
            double blink = Blink((time - blinkStarted) / blinkDuration);
            double eyeClosure = Math.Max(blink, petPose * .96);
            for (int i = 0; i < eyes.Length; i++)
            {
                double shut = Math.Max(SculptedCoat.FaceClosure(sleepPose), eyeClosure);
                eyes[i].ScaleY = .02 + .96 * (1 - shut);
                double closedLine = SmoothStep((shut - .70) / .24);
                closedEyes[i].ScaleX = closedEyes[i].ScaleY = closedEyes[i].ScaleZ = .001 + closedLine * .999;
                eyeRetraction[i].OffsetZ = -.115 * SmoothStep(shut);
                eyeExpression[i].Angle += (((i == 0 ? -1 : 1) * (1 + expression * 2.2)) - eyeExpression[i].Angle) * ease;
                gaze[i].OffsetX = lookX + (i == 0 ? .002 : -.002);
                gaze[i].OffsetY = lookY;
                double pupilScale = sleeping ? 1 : 1 + expression * .018 + curious * .010;
                pupils[i].ScaleX = pupilScale; pupils[i].ScaleY = pupilScale;
                double earAge = time - earListenStarted - i * .06;
                double listen = earAge > 0 && earAge < .8 ? Math.Sin(earAge / .8 * Math.PI) : 0;
                double twitch = listen * (i == (earListenSide < 0 ? 0 : 1) ? 4 : 1.2);
                earTurns[i].Angle += ((sleeping ? (i == 0 ? -6 : 6) : (i == 0 ? 1 : -1) * (1 + petPose * 16 + twitch)) - earTurns[i].Angle) * ease;
            }
            double mouthTarget = sleeping ? 0 : feeding ? feedPose * (.15 + chew * 2.7) : playing ? .08 : happy ? .38 + Math.Sin(time * 5.3) * .08 : .10 + Math.Sin(time * 1.7) * .05;
            jawOpen += (mouthTarget - jawOpen) * ease;
            mouthRest += ((sleeping ? .48 : feeding ? .32 : playing ? .65 : happy ? (corgi ? .65 : .32)
                : corgi ? .86 + sniff * .10 : .48 + sniff * .22) - mouthRest) * ease;
            closedMouth.ScaleX = closedMouth.ScaleY = closedMouth.ScaleZ = .001 + SculptedCoat.FaceClosure(sleepPose) * .999;
            noseBreath.ScaleX = 1 + sniff * .040; noseBreath.ScaleY = 1 - sniff * .020;
            noseMotion.OffsetY = sniff * .0035; noseMotion.OffsetZ = sniff * .003;
            for (int i = 0; i < 4; i++) legAngles[i] = legs[i].Angle;
            sculpt.Update(headSkinTransform.Value, tailSkinTransform.Value, legAngles, breath, jawOpen, expression, mouthRest, sniff, sleepPose, eyeClosure);
        }

        internal const double PlayContactTime = .72;
        // Calibrated against the deformed, furred right-paw surface at contact.
        // The rest pose has clear space; the contact margin is subpixel-sized.
        internal const double PlayBallStartZ = .725;

        internal static double PlayPawGesture(double stateTime)
        {
            if (stateTime <= .22 || stateTime >= 1.28) return 0;
            if (stateTime <= PlayContactTime)
            {
                double t = (stateTime - .22) / (PlayContactTime - .22);
                // Arrive at contact while still moving, then follow through.
                return -9 * SmoothStep(t) - 6 * (t * t * t - t * t);
            }
            if (stateTime < .84)
            {
                double t = (stateTime - PlayContactTime) / (.84 - PlayContactTime);
                return -9 * (2 * t * t * t - 3 * t * t + 1)
                    - 1.44 * (t * t * t - 2 * t * t + t) - 9.8 * (-2 * t * t * t + 3 * t * t);
            }
            return -9.8 * (1 - SmoothStep((stateTime - .84) / (1.28 - .84)));
        }

        public static Vector3D PlayBallMotion(double stateTime)
        {
            double roll = Math.Max(0, Math.Min(1, (stateTime - PlayContactTime) / 3.0));
            double distance = 1 - (1 - roll) * (1 - roll);
            double hop = stateTime > PlayContactTime && stateTime < PlayContactTime + .40
                ? Math.Sin((stateTime - PlayContactTime) * Math.PI / .40) : 0;
            return new Vector3D(distance * 2 - 1, hop, 0);
        }

        static double HeadTarget(double value)
        {
            const double deadZone = .18;
            return Math.Abs(value) <= deadZone ? 0 : Math.Sign(value) * (Math.Abs(value) - deadZone) / (1 - deadZone);
        }

        static double MoveHeadAngle(double current, double target, double dt, double speed)
        {
            double step = (target - current) * (1 - Math.Exp(-dt * 3));
            double limit = speed * dt;
            return current + Math.Max(-limit, Math.Min(limit, step));
        }

        static double Blink(double progress)
        {
            if (progress <= 0 || progress >= 1) return 0;
            // Fast closing, a brief soft contact, and a slower reopening.
            if (progress < .32) return SmoothStep(progress / .32);
            if (progress < .43) return 1;
            return 1 - SmoothStep((progress - .43) / .57);
        }

        void NeckBridge(Model3DGroup parent)
        {
            // An open loft continues the chest into the skull, without a separate
            // spherical neck or a circular shoulder seam. Both ends overlap fur.
            MeshGeometry3D mesh = new MeshGeometry3D();
            const int rings = 9, sides = 32;
            for (int j = 0; j <= rings; j++) for (int k = 0; k <= sides; k++)
            {
                double t = j / (double)rings, a = k * Math.PI * 2 / sides;
                double rx = .300 - Math.Sin(t * Math.PI) * .022, rz = .305 - t * .024;
                mesh.Positions.Add(new Point3D(Math.Cos(a) * rx, .90 + t * .49,
                    .255 + t * .225 + Math.Sin(a) * rz));
                Vector3D n = new Vector3D(Math.Cos(a), -Math.Sin(a) * .40, Math.Sin(a));
                n.Normalize(); mesh.Normals.Add(n);
            }
            for (int j = 0; j < rings; j++) for (int k = 0; k < sides; k++)
            {
                int a = j * (sides + 1) + k, b = a + sides + 1;
                mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b + 1); mesh.TriangleIndices.Add(b);
            }
            AddMesh(parent, mesh, white);
            MeshGeometry3D coat = new MeshGeometry3D();
            for (int i = 0; i < 540; i++)
            {
                double t = (i + .5) / 540.0, a = i * 2.399963;
                double rx = .300 - Math.Sin(t * Math.PI) * .022, rz = .305 - t * .024;
                Point3D root = new Point3D(Math.Cos(a) * rx * .995, .90 + t * .49,
                    .255 + t * .225 + Math.Sin(a) * rz * .995);
                Vector3D n = new Vector3D(Math.Cos(a), -Math.Sin(a) * .40, Math.Sin(a)); n.Normalize();
                Vector3D down = new Vector3D(0, -1, .03);
                Vector3D comb = down - n * Vector3D.DotProduct(down, n); comb.Normalize();
                double len = .075 + random.NextDouble() * .040;
                Lock(coat, root, n, comb, len, len * .055, i);
            }
            AddMesh(parent, coat, white);
        }

        void EyeSurface(Model3DGroup parent, double width, double upper, double lower,
            double z, double depth, double slant, bool almond, Material material)
        {
            // A shallow lens with shaped lids, without a spherical white eyeball.
            MeshGeometry3D mesh = new MeshGeometry3D();
            const int rings = 5, sides = 32;
            mesh.Positions.Add(new Point3D(0, 0, z + depth));
            mesh.Normals.Add(new Vector3D(0, 0, 1));
            for (int j = 1; j <= rings; j++)
            {
                double r = j / (double)rings;
                for (int k = 0; k < sides; k++)
                {
                    double a = k * Math.PI * 2 / sides, s = Math.Sin(a), c = Math.Cos(a);
                    double shape = almond ? .78 + .22 * Math.Abs(s) : 1;
                    mesh.Positions.Add(new Point3D(width * r * c,
                        (s >= 0 ? upper : lower) * r * s * shape + slant * r * c,
                        z + depth * Math.Sqrt(Math.Max(0, 1 - r * r))));
                    Vector3D n = new Vector3D(c * r * .18, s * r * .14, 1); n.Normalize(); mesh.Normals.Add(n);
                }
            }
            for (int k = 0; k < sides; k++)
            {
                mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(1 + k); mesh.TriangleIndices.Add(1 + (k + 1) % sides);
            }
            for (int j = 0; j < rings - 1; j++) for (int k = 0; k < sides; k++)
            {
                int a = 1 + j * sides + k, b = a + sides;
                int c = 1 + j * sides + (k + 1) % sides, d = c + sides;
                mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(c);
                mesh.TriangleIndices.Add(c); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(d);
            }
            AddMesh(parent, mesh, material);
        }

        void Nose(Model3DGroup parent)
        {
            // A soft triangular nose with a narrower underside, rather than a black bead.
            MeshGeometry3D mesh = new MeshGeometry3D();
            for (int i = 0; i < sphere.Positions.Count; i++)
            {
                Point3D p = sphere.Positions[i];
                mesh.Positions.Add(new Point3D(p.X * (.90 + .18 * p.Y), p.Y, p.Z * (1 - .025 * p.Y)));
                mesh.Normals.Add(sphere.Normals[i]);
            }
            mesh.TriangleIndices = sphere.TriangleIndices;
            mesh.Freeze();
            GeometryModel3D nose = new GeometryModel3D(mesh, dark);
            Transform3DGroup tr = new Transform3DGroup();
            tr.Children.Add(new ScaleTransform3D(.100, .066, .073));
            tr.Children.Add(new TranslateTransform3D(0, -.055, .550));
            tr.Freeze(); nose.Transform = tr; nose.Freeze(); parent.Children.Add(nose);
            TriangleCount += mesh.TriangleIndices.Count / 3;
            Material nostril = Matte(10, 12, 14);
            Ball(parent, -.049, -.061, .614, .012, .008, .003, nostril);
            Ball(parent, .049, -.061, .614, .012, .008, .003, nostril);
            Ball(parent, -.026, -.022, .612, .016, .005, .0025, Matte(83, 91, 97));
            Stroke(parent, new Point3D[] { new Point3D(0, -.104, .605), new Point3D(0, -.084, .620),
                new Point3D(0, -.068, .623) }, .0017, nostril);
        }

        void Stroke(Model3DGroup parent, Point3D[] points, double radius, Material material)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            const int sides = 6;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3D axis = points[Math.Min(points.Length - 1, i + 1)] - points[Math.Max(0, i - 1)];
                axis.Normalize();
                Vector3D u = Vector3D.CrossProduct(axis, new Vector3D(0, 0, 1));
                if (u.Length < .01) u = Vector3D.CrossProduct(axis, new Vector3D(0, 1, 0));
                u.Normalize(); Vector3D v = Vector3D.CrossProduct(axis, u); v.Normalize();
                double r = radius * (i == 0 || i == points.Length - 1 ? .65 : 1);
                for (int k = 0; k <= sides; k++)
                {
                    double a = k * Math.PI * 2 / sides;
                    Vector3D n = u * Math.Cos(a) + v * Math.Sin(a);
                    mesh.Positions.Add(points[i] + n * r); mesh.Normals.Add(n);
                }
            }
            for (int i = 0; i < points.Length - 1; i++) for (int k = 0; k < sides; k++)
            {
                int a = i * (sides + 1) + k, b = a + sides + 1;
                mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b + 1); mesh.TriangleIndices.Add(b);
            }
            AddMesh(parent, mesh, material);
        }

        void Ear(Model3DGroup parent, double x, double y, double z, int side)
        {
            Model3DGroup ear = new Model3DGroup();
            Transform3DGroup et = new Transform3DGroup();
            et.Children.Add(new ScaleTransform3D(corgi ? 1.10 : 1, corgi ? 1.32 : .90, corgi ? 1.08 : 1));
            earTurns[side < 0 ? 0 : 1] = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
            et.Children.Add(new RotateTransform3D(earTurns[side < 0 ? 0 : 1]));
            et.Children.Add(new TranslateTransform3D(x, y, z));
            ear.Transform = et;
            parent.Children.Add(ear);
            MeshGeometry3D shell = new MeshGeometry3D();
            // A compact, rounded triangle with a wider root buried in the coat.
            double[] heights = { -.17, -.11, -.04, .04, .13, .22, .30, .36 };
            double[] widths = { .070, .105, .132, .132, .112, .080, .040, .002 };
            double[] offsets = { -.045, -.025, -.005, .015, .035, .050, .060, .065 };
            if (corgi)
            {
                heights = new[] { -.17, -.11, -.04, .04, .13, .21, .27, .30, .31 };
                widths = new[] { .080, .120, .145, .155, .142, .110, .065, .028, .001 };
                offsets = new[] { -.040, -.020, .000, .020, .040, .060, .075, .080, .080 };
            }
            const int rings = 28, sides = 20;
            for (int j = 0; j <= rings; j++)
                for (int k = 0; k <= sides; k++)
                {
                    double a = k * Math.PI * 2 / sides, t = j / (double)rings;
                    int segment = Math.Min(heights.Length - 2, (int)(t * (heights.Length - 1)));
                    double f = t * (heights.Length - 1) - segment;
                    f = f * f * (3 - 2 * f);
                    double h = heights[segment] + (heights[segment + 1] - heights[segment]) * f;
                    double w = widths[segment] + (widths[segment + 1] - widths[segment]) * f;
                    double cx = offsets[segment] + (offsets[segment + 1] - offsets[segment]) * f;
                    Point3D p = new Point3D(side * cx + Math.Cos(a) * w, h,
                        Math.Sin(a) * w * .42 - .018 * t + .045 * (1 - t) * (1 - t));
                    Vector3D along = new Vector3D(side * .08, .52, -.09);
                    Vector3D around = new Vector3D(-Math.Sin(a) * w, 0, Math.Cos(a) * w * .42);
                    Vector3D n = Vector3D.CrossProduct(along, around); n.Normalize();
                    shell.Positions.Add(p); shell.Normals.Add(n);
                }
            for (int j = 0; j < rings; j++)
                for (int k = 0; k < sides; k++)
                {
                    int a = j * (sides + 1) + k, b = a + sides + 1;
                    shell.TriangleIndices.Add(a); shell.TriangleIndices.Add(a + 1); shell.TriangleIndices.Add(b);
                    shell.TriangleIndices.Add(a + 1); shell.TriangleIndices.Add(b + 1); shell.TriangleIndices.Add(b);
                }
            Material earCoat = corgi ? Matte(215, 145, 70) : bright;
            AddMesh(ear, shell, earCoat);
            MeshGeometry3D lining = new MeshGeometry3D();
            for (int j = 0; j <= 7; j++)
            {
                double t = j / 7.0, profile = .12 + t * .70;
                int segment = Math.Min(widths.Length - 2, (int)(profile * (widths.Length - 1)));
                double f = profile * (widths.Length - 1) - segment;
                f = f * f * (3 - 2 * f);
                double radius = widths[segment] + (widths[segment + 1] - widths[segment]) * f;
                double center = side * (offsets[segment] + (offsets[segment + 1] - offsets[segment]) * f);
                double height = heights[segment] + (heights[segment + 1] - heights[segment]) * f;
                double w = radius * (corgi ? .48 : .57) * (1 - .18 * t);
                for (int k = 0; k <= 8; k++)
                {
                    double u = k / 8.0 * 2 - 1;
                    double surface = Math.Sqrt(Math.Max(0, 1 - u * u * w * w / (radius * radius)));
                    lining.Positions.Add(new Point3D(center + u * w, height,
                        radius * .42 * surface + .030 + .0015));
                    lining.Normals.Add(new Vector3D(u * .25, .12, 1));
                    lining.TextureCoordinates.Add(new System.Windows.Point(k / 8.0, t));
                }
            }
            for (int j = 0; j < 7; j++) for (int k = 0; k < 8; k++)
            {
                int a = j * 9 + k, b = a + 9;
                lining.TriangleIndices.Add(a); lining.TriangleIndices.Add(a + 1); lining.TriangleIndices.Add(b);
                lining.TriangleIndices.Add(a + 1); lining.TriangleIndices.Add(b + 1); lining.TriangleIndices.Add(b);
            }
            LinearGradientBrush earSkin = new LinearGradientBrush(corgi ? Color.FromRgb(230, 188, 155) : Color.FromRgb(198, 165, 158),
                corgi ? Color.FromRgb(185, 119, 100) : Color.FromRgb(151, 119, 124), 90);
            earSkin.Freeze(); Material skin = new DiffuseMaterial(earSkin); skin.Freeze(); AddMesh(ear, lining, skin);
            MeshGeometry3D fringe = new MeshGeometry3D();
            for (int edge = -1; edge <= 1; edge += 2)
                for (int j = 0; j < 14; j++)
                {
                    double t = j / 14.0, profile = .16 + t * .76;
                    int segment = Math.Min(widths.Length - 2, (int)(profile * (widths.Length - 1)));
                    double f = profile * (widths.Length - 1) - segment;
                    double width = widths[segment] + (widths[segment + 1] - widths[segment]) * f;
                    double offset = offsets[segment] + (offsets[segment + 1] - offsets[segment]) * f;
                    double height = heights[segment] + (heights[segment + 1] - heights[segment]) * f;
                    Point3D root = new Point3D(side * offset + edge * width, height, .030);
                    Vector3D n = new Vector3D(edge * .65, .7, .15); n.Normalize();
                    Lock(fringe, root, n, new Vector3D(edge * .25, .75, .03),
                        corgi ? .012 + random.NextDouble() * .008 : .024 + random.NextDouble() * .018, corgi ? .003 : .005, j);
                }
            AddMesh(ear, fringe, corgi ? Matte(235, 185, 117) : bright);
        }

        void Fur(Model3DGroup parent, Point3D center, Vector3D radius, int count, double length, string region)
        {
            count = (int)(count * 2.4);
            double golden = Math.PI * (3 - Math.Sqrt(5));
            MeshGeometry3D[] coats = { new MeshGeometry3D(), new MeshGeometry3D(), new MeshGeometry3D(), new MeshGeometry3D() };
            for (int i = 0; i < count; i++)
            {
                double y = 1 - 2 * (i + .5) / count;
                double r = Math.Sqrt(1 - y * y);
                double a = golden * i + (random.NextDouble() - .5) * .13;
                double x = Math.Cos(a) * r, z = Math.Sin(a) * r;
                if (region == "head" && z > .62 && y > -.63 && y < .44) continue;
                if (region == "muzzle" && z > .70 && y > .25) continue;
                if (region == "paw" && y < -.45) continue;
                Point3D root = new Point3D(center.X + x * radius.X * .985, center.Y + y * radius.Y * .985, center.Z + z * radius.Z * .985);
                Vector3D normal = new Vector3D(x / radius.X, y / radius.Y, z / radius.Z); normal.Normalize();
                Vector3D comb = new Vector3D(x * .10, -1, -.22);
                if (region == "head") comb = new Vector3D(x * .95, -.65, -.15);
                if (region == "head" && y > .40) comb = new Vector3D(x * .48, -.22, .86);
                if (region == "chest" || region == "neck" || region == "beard" || region == "cheek") comb = new Vector3D(x * .27, -1, .08);
                if (region == "muzzle") comb = new Vector3D(x * .7, -.6, .22);
                if (region == "tail") comb = new Vector3D(x * .12, .65, -.12);
                if (region == "paw") comb = new Vector3D(x * .3, -.25, .5);
                Vector3D tangent = comb - normal * Vector3D.DotProduct(comb, normal);
                if (tangent.Length < .01) tangent = new Vector3D(.01, 0, -.1);
                tangent.Normalize();
                tangent += new Vector3D(random.NextDouble() * .16 - .08, random.NextDouble() * .08 - .04, random.NextDouble() * .16 - .08);
                tangent.Normalize();
                double len = length * (.55 + random.NextDouble() * .65);
                if (region == "head" && y > .40) len *= 1.08;
                if (region == "head" && z > .62 && y > .40 && y < .60) len *= .78;
                double width = len * (region == "muzzle" ? .046 : .060);
                int material = i % 13 == 0 ? 2 : i % 5 == 0 ? 3 : i % 3 == 0 ? 1 : 0;
                Lock(coats[material], root, normal, tangent, len, width, i);
            }
            Material[] materials = { white, bright, cream, cool };
            for (int j = 0; j < coats.Length; j++) AddMesh(parent, coats[j], materials[j]);
        }

        void TailCoat(Model3DGroup parent, Point3D[] path)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            for (int i = 0; i < 410; i++)
            {
                double t = i / 410.0 * (path.Length - 1);
                int j = Math.Min(path.Length - 2, (int)t);
                double f = t - j, a = i * 2.399963;
                Vector3D axis = path[j + 1] - path[j]; axis.Normalize();
                Vector3D u = new Vector3D(1, 0, 0), v = Vector3D.CrossProduct(axis, u);
                Vector3D n = u * Math.Cos(a) + v * Math.Sin(a); n.Normalize();
                double radius = .071 - .0105 * t;
                Point3D root = path[j] + (path[j + 1] - path[j]) * f + n * radius;
                Lock(mesh, root, n, axis, .065 + random.NextDouble() * .04, .0055, i);
            }
            AddMesh(parent, mesh, white);
        }

        void Lock(MeshGeometry3D mesh, Point3D root, Vector3D normal, Vector3D comb, double length, double width, int seed)
        {
            Vector3D direction = normal * .46 + comb * .89; direction.Normalize();
            Vector3D u = Vector3D.CrossProduct(direction, new Vector3D(.07, 1, .13));
            if (u.Length < .01) u = Vector3D.CrossProduct(direction, new Vector3D(1, 0, 0));
            u.Normalize(); Vector3D v = Vector3D.CrossProduct(direction, u); v.Normalize();
            int start = mesh.Positions.Count;
            for (int j = 0; j < 3; j++)
            {
                double t = j / 2.0;
                Point3D p = root + normal * (length * .31 * t) + comb * (length * .86 * t * t)
                    + u * (Math.Sin(t * Math.PI) * length * .055 * Math.Sin(seed * .71));
                double w = width * Math.Pow(1 - t, .78) + width * .025;
                for (int k = 0; k <= 4; k++)
                {
                    double a = k * Math.PI * 2 / 4 + (seed % 7) * .1;
                    Vector3D radial = u * Math.Cos(a) + v * Math.Sin(a) * .60;
                    mesh.Positions.Add(p + radial * w);
                    Vector3D n = radial * .55 + normal * .75 + direction * .12; n.Normalize(); mesh.Normals.Add(n);
                }
            }
            for (int j = 0; j < 2; j++)
                for (int k = 0; k < 4; k++)
                {
                    int a = start + j * 5 + k, b = a + 5;
                    mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b);
                    mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b + 1); mesh.TriangleIndices.Add(b);
                }
            if (seed % 2 == 0)
            {
                int a = mesh.Positions.Count;
                Point3D mid = root + normal * (length * .22) + comb * (length * .35);
                Point3D tip = root + normal * (length * .38) + comb * (length * 1.1);
                mesh.Positions.Add(root - u * .0014); mesh.Positions.Add(root + u * .0014);
                mesh.Positions.Add(mid - u * .001); mesh.Positions.Add(mid + u * .001); mesh.Positions.Add(tip);
                for (int i = 0; i < 5; i++) mesh.Normals.Add(normal);
                int[] indices = { 0, 1, 2, 1, 3, 2, 2, 3, 4 };
                foreach (int i in indices) mesh.TriangleIndices.Add(a + i);
            }
        }

        void AddMesh(Model3DGroup parent, MeshGeometry3D mesh, Material material)
        {
            if (mesh.Positions.Count == 0) return;
            TriangleCount += mesh.TriangleIndices.Count / 3;
            mesh.Freeze(); GeometryModel3D model = new GeometryModel3D(mesh, material);
            model.BackMaterial = material; model.Freeze(); parent.Children.Add(model);
        }

        void Ball(Model3DGroup parent, double x, double y, double z, double sx, double sy, double sz, Material material, bool smallProp = false)
        {
            MeshGeometry3D mesh = smallProp ? treatSphere : sphere;
            GeometryModel3D m = new GeometryModel3D(mesh, material);
            Transform3DGroup tr = new Transform3DGroup();
            tr.Children.Add(new ScaleTransform3D(sx, sy, sz));
            tr.Children.Add(new TranslateTransform3D(x, y, z));
            tr.Freeze(); m.Transform = tr; m.Freeze(); parent.Children.Add(m);
            TriangleCount += mesh.TriangleIndices.Count / 3;
        }

        void Bone(Model3DGroup parent, Point3D from, Point3D to, double radius, Material material)
        {
            Vector3D d = to - from;
            Vector3D axis = Vector3D.CrossProduct(new Vector3D(0, 1, 0), d);
            Transform3DGroup tr = new Transform3DGroup();
            tr.Children.Add(new ScaleTransform3D(radius, d.Length / 2 + radius * .4, radius));
            if (axis.Length > .0001) tr.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(axis, Vector3D.AngleBetween(new Vector3D(0, 1, 0), d))));
            tr.Children.Add(new TranslateTransform3D((from.X + to.X) / 2, (from.Y + to.Y) / 2, (from.Z + to.Z) / 2));
            tr.Freeze(); GeometryModel3D m = new GeometryModel3D(sphere, material);
            m.Transform = tr; m.Freeze(); parent.Children.Add(m); TriangleCount += sphere.TriangleIndices.Count / 3;
        }

        static Material Matte(byte r, byte g, byte b)
        {
            Material m = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(r, g, b))); m.Freeze(); return m;
        }
        static Material Shiny(byte r, byte g, byte b)
        {
            MaterialGroup m = new MaterialGroup();
            m.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(r, g, b))));
            m.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(115, 119, 121)), 75));
            m.Freeze(); return m;
        }
        static MeshGeometry3D Sphere(int slices, int stacks)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            for (int j = 0; j <= stacks; j++)
            {
                double latitude = Math.PI * j / stacks;
                for (int i = 0; i <= slices; i++)
                {
                    double a = 2 * Math.PI * i / slices;
                    Vector3D n = new Vector3D(Math.Sin(latitude) * Math.Cos(a), Math.Cos(latitude), Math.Sin(latitude) * Math.Sin(a));
                    mesh.Positions.Add(new Point3D(n.X, n.Y, n.Z)); mesh.Normals.Add(n);
                }
            }
            for (int j = 0; j < stacks; j++)
                for (int i = 0; i < slices; i++)
                {
                    int a = j * (slices + 1) + i, b = a + slices + 1;
                    mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b);
                    mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b + 1); mesh.TriangleIndices.Add(b);
                }
            mesh.Freeze(); return mesh;
        }
    }

    // A scene-native toy shares the dog's camera, lighting and depth buffer.
    // Its felt texture and seam rotate with distance travelled, above a ground shadow.
    internal sealed class TennisToy
    {
        internal readonly Model3DGroup Model = new Model3DGroup();
        internal int TriangleCount;
        const double Radius = .115;
        readonly ScaleTransform3D visibility = new ScaleTransform3D(0, 0, 0);
        readonly TranslateTransform3D position = new TranslateTransform3D();
        readonly TranslateTransform3D shadowPosition = new TranslateTransform3D();
        readonly ScaleTransform3D shadowScale = new ScaleTransform3D(1, 1, 1);
        readonly AxisAngleRotation3D spin = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);

        internal TennisToy()
        {
            Model.Transform = visibility;
            Model3DGroup ground = new Model3DGroup();
            Transform3DGroup groundTransform = new Transform3DGroup();
            groundTransform.Children.Add(shadowScale); groundTransform.Children.Add(shadowPosition); ground.Transform = groundTransform;
            RadialGradientBrush shadow = new RadialGradientBrush();
            shadow.GradientStops.Add(new GradientStop(Color.FromArgb(100, 41, 47, 30), 0));
            shadow.GradientStops.Add(new GradientStop(Color.FromArgb(38, 41, 47, 30), .50));
            shadow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 41, 47, 30), 1)); shadow.Freeze();
            MeshGeometry3D disk = new MeshGeometry3D();
            disk.Positions.Add(new Point3D(0, .021, 0)); disk.Normals.Add(new Vector3D(0, 1, 0)); disk.TextureCoordinates.Add(new Point(.5, .5));
            for (int i = 0; i <= 48; i++)
            {
                double a = i * Math.PI * 2 / 48;
                disk.Positions.Add(new Point3D(Math.Cos(a) * .18, .021, Math.Sin(a) * .18));
                disk.Normals.Add(new Vector3D(0, 1, 0)); disk.TextureCoordinates.Add(new Point(.5 + .5 * Math.Cos(a), .5 + .5 * Math.Sin(a)));
                if (i > 0) { disk.TriangleIndices.Add(0); disk.TriangleIndices.Add(i); disk.TriangleIndices.Add(i + 1); }
            }
            Add(ground, disk, new DiffuseMaterial(shadow)); Model.Children.Add(ground);

            Model3DGroup ball = new Model3DGroup();
            Transform3DGroup ballTransform = new Transform3DGroup();
            ballTransform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), 28)));
            ballTransform.Children.Add(new RotateTransform3D(spin)); ballTransform.Children.Add(position); ball.Transform = ballTransform;
            MeshGeometry3D felt = new MeshGeometry3D();
            const int slices = 48, stacks = 24;
            for (int j = 0; j <= stacks; j++) for (int i = 0; i <= slices; i++)
            {
                double lat = Math.PI * j / stacks, lon = Math.PI * 2 * i / slices;
                Vector3D n = new Vector3D(Math.Sin(lat) * Math.Cos(lon), Math.Cos(lat), Math.Sin(lat) * Math.Sin(lon));
                felt.Positions.Add(new Point3D(n.X * Radius, n.Y * Radius, n.Z * Radius));
                felt.Normals.Add(n); felt.TextureCoordinates.Add(new Point(i / (double)slices, j / (double)stacks));
            }
            for (int j = 0; j < stacks; j++) for (int i = 0; i < slices; i++)
            {
                int a = j * (slices + 1) + i, b = a + slices + 1;
                felt.TriangleIndices.Add(a); felt.TriangleIndices.Add(a + 1); felt.TriangleIndices.Add(b);
                felt.TriangleIndices.Add(a + 1); felt.TriangleIndices.Add(b + 1); felt.TriangleIndices.Add(b);
            }
            Add(ball, felt, new DiffuseMaterial(FeltTexture()));
            // The recessed-looking off-white seam is a single continuous spherical curve.
            MeshGeometry3D seam = new MeshGeometry3D();
            const int steps = 160, sides = 6;
            for (int i = 0; i <= steps; i++)
            {
                double t = i * Math.PI * 2 / steps;
                Point3D p = SeamPoint(t);
                Vector3D normal = new Vector3D(p.X, p.Y, p.Z); normal.Normalize();
                Vector3D tangent = SeamPoint(t + .001) - SeamPoint(t - .001); tangent.Normalize();
                Vector3D side = Vector3D.CrossProduct(tangent, normal); side.Normalize();
                for (int k = 0; k <= sides; k++)
                {
                    double angle = k * Math.PI * 2 / sides;
                    Vector3D radial = normal * Math.Cos(angle) + side * Math.Sin(angle);
                    seam.Positions.Add(p + radial * .0030); seam.Normals.Add(radial);
                    if (i < steps && k < sides)
                    {
                        int a = i * (sides + 1) + k, b = a + sides + 1;
                        seam.TriangleIndices.Add(a); seam.TriangleIndices.Add(a + 1); seam.TriangleIndices.Add(b);
                        seam.TriangleIndices.Add(a + 1); seam.TriangleIndices.Add(b + 1); seam.TriangleIndices.Add(b);
                    }
                }
            }
            Add(ball, seam, new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(230, 225, 181))));
            Model.Children.Add(ball);
        }

        static Point3D SeamPoint(double t)
        {
            double latitude = .67 * Math.Sin(2 * t), radius = Radius - .0007;
            return new Point3D(radius * Math.Cos(latitude) * Math.Cos(t), radius * Math.Sin(latitude), radius * Math.Cos(latitude) * Math.Sin(t));
        }

        static Brush FeltTexture()
        {
            const int width = 512, height = 256;
            byte[] pixels = new byte[width * height * 4]; Random noise = new Random(316);
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                double nap = Math.Sin(x * .19 + y * .73) * 2.5;
                int grain = noise.Next(-9, 10) + (int)nap, index = (y * width + x) * 4;
                pixels[index] = (byte)Math.Max(0, Math.Min(255, 68 + grain));
                pixels[index + 1] = (byte)Math.Max(0, Math.Min(255, 214 + grain));
                pixels[index + 2] = (byte)Math.Max(0, Math.Min(255, 194 + grain)); pixels[index + 3] = 255;
            }
            BitmapSource texture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); texture.Freeze();
            ImageBrush brush = new ImageBrush(texture); brush.Freeze(); return brush;
        }

        void Add(Model3DGroup parent, MeshGeometry3D mesh, Material material)
        {
            TriangleCount += mesh.TriangleIndices.Count / 3; mesh.Freeze(); material.Freeze();
            GeometryModel3D geometry = new GeometryModel3D(mesh, material); geometry.BackMaterial = material; geometry.Freeze(); parent.Children.Add(geometry);
        }

        internal void Update(bool active, Vector3D motion)
        {
            visibility.ScaleX = visibility.ScaleY = visibility.ScaleZ = active ? 1 : 0;
            double distance = (motion.X + 1) * .30;
            position.OffsetX = .245; position.OffsetY = .024 + Radius + motion.Y * .012; position.OffsetZ = WestieModel.PlayBallStartZ + distance;
            spin.Angle = distance / Radius * 180 / Math.PI;
            shadowPosition.OffsetX = position.OffsetX; shadowPosition.OffsetZ = position.OffsetZ;
            shadowScale.ScaleX = shadowScale.ScaleZ = 1 + motion.Y * .12;
        }
    }
}
