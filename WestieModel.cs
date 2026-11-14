using System;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows;
using System.Windows.Media.Imaging;

namespace WestiePet
{
    // An original, articulated terrier. Combed tapered locks share a few meshes.
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
        internal Rect3D DogBounds { get { return dog.Bounds; } }

        public WestieModel()
        {
            Scene.Children.Add(new AmbientLight(Color.FromRgb(145, 150, 158)));
            Scene.Children.Add(new DirectionalLight(Color.FromRgb(128, 123, 116), new Vector3D(-2, -3, -4)));
            Scene.Children.Add(new DirectionalLight(Color.FromRgb(78, 91, 108), new Vector3D(3, -1, 1)));
            Scene.Children.Add(new DirectionalLight(Color.FromRgb(105, 111, 113), new Vector3D(-1, -1, 4)));
            Scene.Children.Add(dog);
            Transform3DGroup transform = new Transform3DGroup();
            transform.Children.Add(new RotateTransform3D(Heading));
            transform.Children.Add(lift);
            dog.Transform = transform;

            sculpt = new SculptedCoat(white, mouth, new Material[] { white, bright, cool });
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
            ht.Children.Add(new TranslateTransform3D(0, 1.345, .45));
            ht.Children.Add(headSkinTransform);
            head.Transform = ht;
            dog.Children.Add(head);

            Ear(head, -.250, .355, -.045, -1);
            Ear(head, .250, .355, -.045, 1);

            for (int i = 0; i < 2; i++)
            {
                double x = i == 0 ? -.125 : .125;
                Model3DGroup eye = new Model3DGroup();
                Transform3DGroup et = new Transform3DGroup();
                eyes[i] = new ScaleTransform3D(1, 1, 1);
                et.Children.Add(eyes[i]);
                eyeExpression[i] = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
                et.Children.Add(new RotateTransform3D(eyeExpression[i]));
                eyeRetraction[i] = new TranslateTransform3D(); et.Children.Add(eyeRetraction[i]);
                et.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), i == 0 ? -8 : 8)));
                et.Children.Add(new TranslateTransform3D(x, .170, .345));
                eye.Transform = et;
                head.Children.Add(eye);
                // Keep a soft white rim around the iris, with a slightly heavier upper lid.
                double slant = i == 0 ? .006 : -.006;
                EyeSurface(eye, .093, .097, .083, .021, .029, slant, false, sclera);
                Point3D[] lid = new Point3D[10];
                for (int k = 0; k < lid.Length; k++)
                {
                    double a = k * Math.PI / (lid.Length - 1);
                    lid[k] = new Point3D(Math.Cos(a) * .093,
                        Math.Sin(a) * .097 + slant * Math.Cos(a), .025);
                }
                Stroke(eye, lid, .0034, Matte(83, 73, 61));
                Point3D[] lowerLid = new Point3D[10];
                for (int k = 0; k < lowerLid.Length; k++)
                {
                    double a = Math.PI + k * Math.PI / (lowerLid.Length - 1);
                    lowerLid[k] = new Point3D(Math.Cos(a) * .093,
                        Math.Sin(a) * .083 + slant * Math.Cos(a), .025);
                }
                Stroke(eye, lowerLid, .0019, Matte(155, 147, 132));
                Model3DGroup pupil = new Model3DGroup();
                gaze[i] = new TranslateTransform3D();
                pupils[i] = new ScaleTransform3D(1, 1, 1);
                Transform3DGroup pupilTransform = new Transform3DGroup();
                pupilTransform.Children.Add(pupils[i]); pupilTransform.Children.Add(gaze[i]);
                pupil.Transform = pupilTransform; eye.Children.Add(pupil);
                EyeSurface(pupil, .057, .068, .059, .052, .006, 0, false, iris);
                EyeSurface(pupil, .047, .056, .049, .060, .004, 0, false, dark);
                Ball(pupil, -.020, .022, .067, .012, .014, .004, Matte(255, 255, 253));
                Ball(pupil, .020, -.028, .066, .005, .006, .0025, bright);

                Model3DGroup closed = new Model3DGroup();
                closedEyes[i] = new ScaleTransform3D(.001, .001, .001);
                Transform3DGroup ct = new Transform3DGroup(); ct.Children.Add(closedEyes[i]);
                ct.Children.Add(new TranslateTransform3D(x, .170, .345)); closed.Transform = ct;
                Point3D[] shutLid = new Point3D[13];
                for (int k = 0; k < shutLid.Length; k++)
                {
                    double lx = -.066 + k * .132 / (shutLid.Length - 1), r = lx / .066;
                    Point3D p = SculptedCoat.ClosedFacePoint(new Point3D(x + lx, 1.508 - .012 * (1 - r * r), .82), .002);
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
                Point3D p = SculptedCoat.ClosedFacePoint(new Point3D(x, 1.115 + .35 * x * x, .98), .0015);
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

        public void Animate(double time, string state, double stateTime)
        {
            bool walking = state == "walk", sleeping = state == "sleep", playing = state == "play";
            bool petting = state == "pet", feeding = state == "feed";
            bool moving = walking && !WalkingPaused;
            bool happy = state == "pet" || state == "feed" || state == "play";
            double dt = double.IsNaN(previousTime) || time <= previousTime ? .042 : Math.Min(.10, time - previousTime);
            previousTime = time;
            double ease = 1 - Math.Exp(-dt * 9);
            sleepPose += ((sleeping ? 1 : 0) - sleepPose) * (1 - Math.Exp(-dt * 4.6));
            expression += ((happy ? 1 : 0) - expression) * ease;
            attention += (((sleeping || walking || playing || feeding || petting) ? 0 : attentionStrength) - attention) * (1 - Math.Exp(-dt * 5));
            double petPose = petting ? SmoothStep(stateTime / .30) * SmoothStep((3.6 - stateTime) / .55) : 0;
            double feedPose = feeding ? SmoothStep(stateTime / .6) * SmoothStep((4.5 - stateTime) / .65) : 0;
            double nuzzle = Math.Sin(stateTime * 4.5);
            double chew = feeding && stateTime > .85 && stateTime < 3.95 ? .5 + .5 * Math.Sin((stateTime - .85) * Math.PI * 2 / .75) : 0;
            AnimateTreat(feeding, stateTime);
            Vector3D playBall = PlayBallMotion(stateTime);
            playToy.Update(playing, playBall);
            double curiosityTarget = sleeping ? 0 : (state == "play" ? .85 : .24 + attention * .55);
            curious += (curiosityTarget - curious) * ease;
            // Short glances have a pause between them; both eyes share the same target.
            double glance = Math.Floor(time / 2.6);
            double aimX = sleeping ? 0 : happy ? -.006 + Math.Sin(time * .8) * .004
                : walking ? -.010 : Math.Sin(glance * 2.17 + .6) * .022;
            double aimY = sleeping ? 0 : happy ? .010 : .005 + Math.Sin(glance * 1.63 + 1.1) * .008;
            aimX = aimX * (1 - attention) + attentionX * .023 * attention;
            aimY = aimY * (1 - attention) + (.004 + attentionY * .012) * attention;
            if (playing) { aimX = playBall.X * .021; aimY = -.011 + playBall.Y * .003; }
            double eyeEase = 1 - Math.Exp(-dt * 13);
            lookX += (aimX - lookX) * eyeEase; lookY += (aimY - lookY) * eyeEase;
            // Eye glances do not move the skull. Cursor-driven head turns have
            // a dead zone and an independent, slower target filter.
            double headEase = 1 - Math.Exp(-dt * 2);
            headLookX += ((playing ? playBall.X : HeadTarget(attentionX) * attention) - headLookX) * headEase;
            headLookY += ((playing ? -.5 : HeadTarget(attentionY) * attention) - headLookY) * headEase;
            double sniffPhase = time % (happy ? 6.8 : 9.2);
            double sniffTarget = !sleeping && sniffPhase > 6.0 && sniffPhase < 6.65
                ? Math.Sin((sniffPhase - 6.0) / .65 * Math.PI) * (.5 + .5 * Math.Sin(time * 27)) : 0;
            sniff += (sniffTarget - sniff) * (1 - Math.Exp(-dt * 20));
            double bounce = moving ? Math.Abs(Math.Sin(time * 8)) * .025 : Math.Sin(time * 2.2) * .002;
            lift.OffsetY += ((-.36 * sleepPose + (1 - sleepPose) * bounce) - lift.OffsetY) * ease;
            headRest.OffsetX += ((petPose * .045 * nuzzle) - headRest.OffsetX) * ease;
            headRest.OffsetY += ((-.43 * sleepPose + .035 * petPose - .075 * feedPose) - headRest.OffsetY) * ease;
            headRest.OffsetZ += ((.060 * sleepPose + .045 * petPose + .065 * feedPose) - headRest.OffsetZ) * ease;
            double breath = Math.Sin(time * (sleeping ? 1.7 : 2.2));
            double tiltTarget = sleeping ? -4 : petting ? petPose * (-7 + nuzzle * 11) : -headLookX * .75;
            double nodTarget = feeding ? feedPose * (23 + chew * 5)
                : petting ? petPose * (-10 + Math.Cos(stateTime * 4.5) * 4)
                : sleeping ? 8 : playing ? 7 - playBall.Y * .8 : -1.5 + breath * .20 - headLookY * .7;
            double aimYaw = walking ? (Heading.Angle < 0 ? 12 : -12) : -19 + (sleeping ? -3 : headLookX * (playing ? 7 : 2.5));
            if (petting) aimYaw += petPose * nuzzle * 9;
            headTilt.Angle = MoveHeadAngle(headTilt.Angle, tiltTarget, dt, petting ? 60 : 9);
            headNod.Angle = MoveHeadAngle(headNod.Angle, nodTarget, dt, feeding || petting ? 52 : 14);
            headYaw.Angle = MoveHeadAngle(headYaw.Angle, aimYaw, dt, 12);
            double wagSpeed = playing ? 8 : happy ? 15 : walking ? 9 : 4;
            double wagTime = petting || feeding ? stateTime : time;
            double wag = Math.Sin(wagTime * wagSpeed) + Math.Sin(wagTime * wagSpeed * 2 + .4) * .14;
            tailWag.Angle += ((sleeping ? 54 : wag * (petting ? 38 : playing ? 14 : happy ? 25 : walking ? 15 : 8)) - tailWag.Angle) * ease;
            tailRest.Angle = -65 * sleepPose;
            for (int i = 0; i < legs.Length; i++)
            {
                double tapPhase = Math.Max(0, Math.Min(1, (stateTime - .15) / .55));
                double pawTap = playing && i == 1 ? -9 * Math.Sin(tapPhase * Math.PI) : 0;
                legs[i].Angle += (((1 - sleepPose) * (moving ? Math.Sin(time * 8 + (i == 0 || i == 3 ? 0 : Math.PI)) * 23 : pawTap)
                    + sleepPose * -70) - legs[i].Angle) * ease;
            }
            for (int i = 0; i < eyes.Length; i++)
            {
                double blink = Blink(time - i * .018);
                double eyeHeight = sleeping ? .07 : (.96 + curious * .06 - expression * .09) * (1 - blink * .94);
                eyeHeight *= 1 - petPose * .78;
                eyes[i].ScaleY = eyeHeight;
                double shut = SculptedCoat.FaceClosure(sleepPose);
                closedEyes[i].ScaleX = closedEyes[i].ScaleY = closedEyes[i].ScaleZ = .001 + shut * .999;
                eyeRetraction[i].OffsetZ = -.115 * shut;
                eyeExpression[i].Angle += (((i == 0 ? -1 : 1) * (1 + expression * 2.2)) - eyeExpression[i].Angle) * ease;
                gaze[i].OffsetX = lookX + (i == 0 ? .002 : -.002);
                gaze[i].OffsetY = lookY;
                double pupilScale = sleeping ? 1 : 1.01 + expression * .05 + curious * .025 + Math.Sin(time * 1.1) * .010;
                pupils[i].ScaleX = pupilScale; pupils[i].ScaleY = pupilScale;
                double earPhase = (time + i * 2.7) % 9.3;
                double twitch = earPhase < .42 ? Math.Sin(earPhase / .42 * Math.PI * 2) * 5 : 0;
                earTurns[i].Angle += ((sleeping ? (i == 0 ? -6 : 6) : (i == 0 ? 1 : -1) * (2 + petPose * 16) + twitch) - earTurns[i].Angle) * ease;
            }
            double mouthTarget = sleeping ? 0 : feeding ? feedPose * (.15 + chew * 2.7) : playing ? .08 : happy ? .38 + Math.Sin(time * 5.3) * .08 : .10 + Math.Sin(time * 1.7) * .05;
            jawOpen += (mouthTarget - jawOpen) * ease;
            mouthRest += ((sleeping ? .48 : playing ? .65 : happy ? .32 : .48 + sniff * .22) - mouthRest) * ease;
            closedMouth.ScaleX = closedMouth.ScaleY = closedMouth.ScaleZ = .001 + SculptedCoat.FaceClosure(sleepPose) * .999;
            noseBreath.ScaleX = 1 + sniff * .040; noseBreath.ScaleY = 1 - sniff * .020;
            noseMotion.OffsetY = sniff * .0035; noseMotion.OffsetZ = sniff * .003;
            for (int i = 0; i < 4; i++) legAngles[i] = legs[i].Angle;
            sculpt.Update(headSkinTransform.Value, tailSkinTransform.Value, legAngles, breath, jawOpen, expression, mouthRest, sniff, sleepPose);
        }

        public static Vector3D PlayBallMotion(double stateTime)
        {
            double roll = Math.Max(0, Math.Min(1, (stateTime - .45) / 3.0));
            double distance = 1 - (1 - roll) * (1 - roll);
            double hop = stateTime > .45 && stateTime < .95 ? Math.Sin((stateTime - .45) * Math.PI / .50) : 0;
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

        static double Blink(double time)
        {
            double t = time % 5.3;
            if (t > 3.82 && t < 4.12) return Math.Sin((t - 3.82) / .30 * Math.PI);
            if (t > 4.32 && t < 4.49) return Math.Sin((t - 4.32) / .17 * Math.PI) * .78;
            return 0;
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
            et.Children.Add(new ScaleTransform3D(1, .90, 1));
            earTurns[side < 0 ? 0 : 1] = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
            et.Children.Add(new RotateTransform3D(earTurns[side < 0 ? 0 : 1]));
            et.Children.Add(new TranslateTransform3D(x, y, z));
            ear.Transform = et;
            parent.Children.Add(ear);
            MeshGeometry3D shell = new MeshGeometry3D();
            double[] heights = { -.045, .04, .17, .29, .365, .388 };
            double[] widths = { .151, .143, .105, .060, .020, .0015 };
            for (int j = 0; j < heights.Length; j++)
                for (int k = 0; k <= 16; k++)
                {
                    double a = k * Math.PI * 2 / 16, t = j / 5.0;
                    shell.Positions.Add(new Point3D(side * .055 * t + Math.Cos(a) * widths[j], heights[j], Math.Sin(a) * widths[j] * .53 - t * .025));
                    Vector3D n = new Vector3D(Math.Cos(a), .30, Math.Sin(a) * 1.5); n.Normalize(); shell.Normals.Add(n);
                }
            for (int j = 0; j < heights.Length - 1; j++)
                for (int k = 0; k < 16; k++)
                {
                    int a = j * 17 + k, b = a + 17;
                    shell.TriangleIndices.Add(a); shell.TriangleIndices.Add(a + 1); shell.TriangleIndices.Add(b);
                    shell.TriangleIndices.Add(a + 1); shell.TriangleIndices.Add(b + 1); shell.TriangleIndices.Add(b);
                }
            AddMesh(ear, shell, bright);
            MeshGeometry3D lining = new MeshGeometry3D();
            for (int j = 0; j <= 7; j++)
            {
                double t = j / 7.0, w = .067 * Math.Pow(1 - t, .75);
                for (int k = 0; k <= 8; k++)
                {
                    double u = k / 8.0 * 2 - 1;
                    lining.Positions.Add(new Point3D(side * (.012 + .039 * t) + u * w,
                        .045 + t * .26, .071 - .054 * t * t + (1 - u * u) * .008));
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
            LinearGradientBrush earSkin = new LinearGradientBrush(Color.FromRgb(198, 165, 158), Color.FromRgb(151, 119, 124), 90);
            earSkin.Freeze(); Material skin = new DiffuseMaterial(earSkin); skin.Freeze(); AddMesh(ear, lining, skin);
            MeshGeometry3D fringe = new MeshGeometry3D();
            for (int edge = -1; edge <= 1; edge += 2)
                for (int j = 0; j < 22; j++)
                {
                    double t = j / 22.0;
                    Point3D root = new Point3D(edge * .145 * (1 - t) + side * .055 * t, .365 * t, .009);
                    Vector3D n = new Vector3D(edge * .7, .6, .15); n.Normalize();
                    Lock(fringe, root, n, new Vector3D(edge * .3, .7, .03), .04 + random.NextDouble() * .035, .009, j);
                }
            AddMesh(ear, fringe, bright);
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
            position.OffsetX = .245; position.OffsetY = .024 + Radius + motion.Y * .012; position.OffsetZ = .60 + distance;
            spin.Angle = distance / Radius * 180 / Math.PI;
            shadowPosition.OffsetX = position.OffsetX; shadowPosition.OffsetZ = position.OffsetZ;
            shadowScale.ScaleX = shadowScale.ScaleZ = 1 + motion.Y * .12;
        }
    }
}
