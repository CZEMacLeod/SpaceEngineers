#region Prelude
using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRageMath;

// Change this namespace for each script you create.
namespace SpaceEngineers.UWBlockPrograms.TankLevers
{
    public sealed class Program : MyGridProgram
    {
        // Your code goes between the next #endregion and #region
        #endregion

        /*
         * 
         * My Little Tank
         * 
         * Script to provide support for tank steer levers, and direct drive wheels.
         * 
         * Requires 2 hinges whose names include the words Tank, Control, and either Left or Right.
         * The default gear ration is x1.0 but a third hinge with Tank, Control, and Gear can be added.
         * 
         * If the direction of the hinge needs to be reversed, then include the word Reversed.
         * 
         * Any number of output rotors can be controlled as long as they have the words Tank, Drive, and either Left or Right.
         * The constants at the top of the script can be adjusted if you want to make your tank faster or slower.
         * 
         * The target speed of the rotors is 'angle in degrees' * RPMPerDegree * gearRatio.
         * gearRatio is x1.0 unless you build the gear lever, in which case it is 'angle in degrees' * GearRatioPerDegree.
         * Note that negative values will put things in reverse.
         * 
         * You will either have to build your hinges rotated 180°, or use Reversed on one of the hinges or you will find yourself going in circles.
         * The gear lever supports having Reversed in the name too.
         * 
         * The output of the script can be shown on the programmable block if it is in text mode.
         * 
         * You can also add 1 or more LCD Panels with the words Tank and LCD in them.
         * You can also add one Left, Right, Gear, or leave those out to filter the information.
         * This allows you to display the details of each lever on the lever.
         * 
         * There are 2 more features:
         * Each lever has a DeadZone (setting below) in which it is assumed to be 0° to allow you to stop more easily.
         * If the lever is at exactly 0° then the hinge will be switched off, and the limits set to +-90°
         * This allows you to add all the hinges to a group and use a timer block to turn them on, then move to 0°.
         * This provides a nice 'handbrake' or 'emergency stop'.
         * 
         * You probably want to turn the programmable block off with a presence sensor to prevent runaways.
         * 
         * Version 1.1
         * Date 06-Dec-2024
         * 
         */

        private const float RPMPerDegree = 1;
        private const float GearRatioPerDegree = 0.02F;  // 90° => x1.8
        private const float DeadZone = 5; // +- degrees

        class Hinge
        {
            public IMyMotorStator hinge;
            public bool reversed;
            public List<IMyMotorStator> rotors;
            public float lastSpeed;
            public bool left;
            public bool right;
            public bool gear;
        }

        class Display
        {
            public IMyTextPanel textPanel;
            public bool left;
            public bool right;
            internal bool gear;
        }

        private List<T> GetBlockOfType<T>(params string[] tags)
            where T : class, IMyTerminalBlock
        {
            var list = new List<T>();
            GridTerminalSystem.GetBlocksOfType(list, x =>
                {
                    if (!x.IsSameConstructAs(Me)) return false;
                    var names = x.CustomName.Split(' ');
                    return tags.All(tag => names.Equals(tag));
                });
            return list;
        }

        private readonly List<Display> displays = new List<Display>();
        private readonly List<Hinge> controlHinges = new List<Hinge>();
        public Program()
        {
            var textPanels = GetBlockOfType<IMyTextPanel>("Tank", "LCD");
            foreach (var textPanel in textPanels)
            {
                var names = textPanel.CustomName.Split(' ');
                this.displays.Add(new Display()
                {
                    left = names.Contains("Left"),
                    right = names.Contains("Right"),
                    gear = names.Contains("Gear"),
                    textPanel = textPanel
                });
            }

            var rotorsLeft = GetBlockOfType<IMyMotorStator>("Tank", "Drive", "Left");
            var rotorsRight = GetBlockOfType<IMyMotorStator>("Tank", "Drive", "Right");

            var gearHinges = GetBlockOfType<IMyMotorStator>("Tank", "Control", "Gear");
            AddHinges(gearHinges, h=>h.gear=true);

            var leftHinges = GetBlockOfType<IMyMotorStator>("Tank", "Control", "Left");
            AddHinges(leftHinges, h =>
            {
                h.left = true;
                h.rotors = rotorsLeft;
            });

            var rightHinges = GetBlockOfType<IMyMotorStator>("Tank", "Control", "Right");
            AddHinges(rightHinges, h =>
            {
                h.right = true;
                h.rotors = rotorsRight;
            });

            //This makes the program automatically run every 10 ticks.
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }

        private void AddHinges(List<IMyMotorStator> hinges, Action<Hinge> configure)
        {
            foreach (var hinge in hinges)
            {
                var h = new Hinge()
                {
                    hinge = hinge,
                    reversed = hinge.CustomName.Contains("Reversed")
                };
                configure(h);
                controlHinges.Add(h);
            }
        }

        private const float DegreesPerRadian = (float)(180 / Math.PI);
        private float gearRatio = 1.0F;

        public void Main()
        {
            var sbAll = new StringBuilder();

            var sbGear = new StringBuilder();
            var sbLeft = new StringBuilder();
            var sbRight = new StringBuilder();

            foreach (var hinge in controlHinges)
            {
                var actualHinge = hinge.hinge;
                var angle = MyMath.NormalizeAngle(actualHinge.Angle) * DegreesPerRadian;
                // Unlock at 0°
                if (angle == 0)
                {
                    actualHinge.Enabled = false;
                    actualHinge.LowerLimitDeg = -90;
                    actualHinge.UpperLimitDeg = 90;
                }
                // Dead zone in middle
                if (angle > -DeadZone & angle < DeadZone) angle = 0;

                var speed = angle * RPMPerDegree * gearRatio;
                if (hinge.reversed) { speed = -speed; }

                if (hinge.left)
                {
                    sbLeft.Append("Left @ ");
                    AddStatus(sbLeft, angle, speed);
                }
                if (hinge.right)
                {
                    sbRight.Append("Right @ ");
                    AddStatus(sbRight, angle, speed);
                }
                if (hinge.gear)
                {
                    var gear = angle * GearRatioPerDegree;
                    if (hinge.reversed) { gear = -gear; }
                    sbGear.Append("Gear ");
                    sbGear.AppendFormat("{0:F1}", angle);
                    sbGear.AppendLine("°");
                    sbGear.Append("\tGear Ratio: ");
                    sbGear.AppendFormat("x {0:F2}", gear);
                    sbGear.AppendLine();
                    this.gearRatio = gear;
                }
                else
                {
                    if (speed != hinge.lastSpeed)
                    {
                        hinge.lastSpeed = speed;
                        foreach (var rotor in hinge.rotors)
                        {
                            sbAll.Append(rotor.CustomName);
                            sbAll.Append(" @ ");
                            sbAll.AppendFormat("{0:F2}", rotor.TargetVelocityRPM);
                            sbAll.AppendLine("rpm");
                            rotor.TargetVelocityRPM = speed;
                        }
                    }
                }
            }
            sbAll.Append(sbLeft.ToString());
            sbAll.Append(sbRight.ToString());
            sbAll.Append(sbGear.ToString());

            var statusAll = sbAll.ToString();
            if (displays != null)
            {
                foreach (var display in displays)
                {
                    display.textPanel.WriteText("");
                    if (display.left) { display.textPanel.WriteText(sbLeft.ToString(), true); }
                    if (display.right) { display.textPanel.WriteText(sbRight.ToString(), true); }
                    if (display.gear) { display.textPanel.WriteText(sbGear.ToString(), true); }
                    if (!display.gear && !display.left && !display.right)
                    {
                        display.textPanel.WriteText(statusAll, true);
                    }
                }
            }
            IMyTextSurface textDisplay = Me.GetSurface(0);
            textDisplay.WriteText(statusAll);
            Echo(statusAll);
        }

        private static void AddStatus(StringBuilder sb, float angle, float speed)
        {
            sb.AppendFormat("{0:F1}", angle);
            sb.AppendLine("°");
            sb.Append("\tTarget Speed: ");
            sb.AppendFormat("{0:F2}", speed);
            sb.AppendLine("rpm");
        }

        #region PreludeFooter
    }
}
#endregion