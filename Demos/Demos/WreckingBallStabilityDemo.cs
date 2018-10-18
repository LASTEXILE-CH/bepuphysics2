﻿using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using DemoContentLoader;
using DemoRenderer;
using DemoRenderer.UI;
using DemoUtilities;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Demos.Demos
{
    /// <summary>
    /// Shows a few different ways of making a rope with a heavy thing attached not freak out.
    /// </summary>
    public class WreckingBallStabilityDemo : Demo
    {
        int[] BuildRopeBodies(in Vector3 start, int bodyCount, float bodySize, float bodySpacing, float massPerBody, float inverseInertiaScale)
        {
            int[] handles = new int[bodyCount + 1];
            var ropeShape = new Sphere(bodySize);
            ropeShape.ComputeInertia(massPerBody, out var ropeInertia);
            Symmetric3x3.Scale(ropeInertia.InverseInertiaTensor, inverseInertiaScale, out ropeInertia.InverseInertiaTensor);
            var ropeShapeIndex = Simulation.Shapes.Add(ropeShape);
            //Build the links.
            var bodyDescription = new BodyDescription
            {
                //Make the uppermost block kinematic to hold up the rest of the chain.
                Activity = new BodyActivityDescription(.01f),
                Collidable = new CollidableDescription(ropeShapeIndex, 0.1f),
            };
            for (int linkIndex = 0; linkIndex < bodyCount + 1; ++linkIndex)
            {
                bodyDescription.LocalInertia = linkIndex == 0 ? new BodyInertia() : ropeInertia;
                bodyDescription.Pose = new RigidPose(start - new Vector3(0, linkIndex * (bodySpacing + 2 * bodySize), 0));
                handles[linkIndex] = Simulation.Bodies.Add(bodyDescription);
            }

            return handles;
        }
        int[] BuildRope(in Vector3 start, int bodyCount, float bodySize, float bodySpacing, float constraintOffsetLength, float massPerBody, float inverseInertiaScale, SpringSettings springSettings)
        {
            var handles = BuildRopeBodies(start, bodyCount, bodySize, bodySpacing, massPerBody, inverseInertiaScale);
            var maximumDistance = 2 * bodySize + bodySpacing - 2 * constraintOffsetLength;
            for (int i = 0; i < handles.Length - 1; ++i)
            {
                Simulation.Solver.Add(handles[i], handles[i + 1],
                    new DistanceLimit(new Vector3(0, -constraintOffsetLength, 0), new Vector3(0, constraintOffsetLength, 0), maximumDistance * 0.1f, maximumDistance, springSettings));
            }
            return handles;
        }

        int CreateWreckingBall(int[] bodyHandles, float ropeBodyRadius, float bodySpacing, float wreckingBallRadius, BodyInertia wreckingBallInertia, TypedIndex wreckingBallShapeIndex)
        {
            var lastBodyReference = new BodyReference(bodyHandles[bodyHandles.Length - 1], Simulation.Bodies);
            var wreckingBallPosition = lastBodyReference.Pose.Position - new Vector3(0, ropeBodyRadius + bodySpacing + wreckingBallRadius, 0);
            var description = new BodyDescription(wreckingBallPosition, wreckingBallInertia, wreckingBallShapeIndex, 0.1f, new BodyActivityDescription(0.01f));
            //Give it a little bump.
            description.Velocity = new BodyVelocity(new Vector3(-10, 0, 0), default);
            var wreckingBallBodyHandle = Simulation.Bodies.Add(description);
            return wreckingBallBodyHandle;
        }

        int AttachWreckingBall(int[] bodyHandles, float ropeBodyRadius, float bodySpacing, float constraintOffsetLength, float wreckingBallRadius, BodyInertia wreckingBallInertia, TypedIndex wreckingBallShapeIndex, SpringSettings springSettings)
        {
            int wreckingBallBodyHandle = CreateWreckingBall(bodyHandles, ropeBodyRadius, bodySpacing, wreckingBallRadius, wreckingBallInertia, wreckingBallShapeIndex);
            var maximumDistance = bodySpacing + ropeBodyRadius - constraintOffsetLength;
            Simulation.Solver.Add(bodyHandles[bodyHandles.Length - 1], wreckingBallBodyHandle,
                new DistanceLimit(new Vector3(0, -constraintOffsetLength, 0), new Vector3(0, wreckingBallRadius, 0), maximumDistance * 0.1f, maximumDistance, springSettings));
            return wreckingBallBodyHandle;
        }

        RolloverInfo rolloverInfo;


        public unsafe override void Initialize(ContentArchive content, Camera camera)
        {
            camera.Position = new Vector3(0, 25, 80);
            camera.Yaw = 0;
            camera.Pitch = 0;

            Simulation = Simulation.Create(BufferPool, new TestCallbacks());
            Simulation.PoseIntegrator.Gravity = new Vector3(0, -10, 0);
            rolloverInfo = new RolloverInfo();
            var smallWreckingBall = new Sphere(1);
            smallWreckingBall.ComputeInertia(5, out var smallWreckingBallInertia);
            var smallWreckingBallIndex = Simulation.Shapes.Add(smallWreckingBall);
            {
                //The first thing you might try when building a rope is a simple chain of low mass bodies connected by distance limits with the wrecking ball attached at the end in a natural way.
                var startLocation = new Vector3(-55, 35, 0);
                const float bodySpacing = 0.3f;
                const float bodyRadius = 0.5f;
                var springSettings = new SpringSettings(30, 1);
                var bodyHandles = BuildRope(startLocation, 12, bodyRadius, bodySpacing, bodyRadius, 1, 1, springSettings);

                //With a small wrecking ball, this actually works fine with reasonable spring settings.
                AttachWreckingBall(bodyHandles, bodyRadius, bodySpacing, bodyRadius, smallWreckingBall.Radius, smallWreckingBallInertia, smallWreckingBallIndex, springSettings);
                rolloverInfo.Add(startLocation + new Vector3(0, 2, 0), "Naive, 5:1 mass ratio");
            }
            var bigWreckingBall = new Sphere(3);
            //This wrecking ball is much, much heavier.
            bigWreckingBall.ComputeInertia(100, out var bigWreckingBallInertia);
            var bigWreckingBallIndex = Simulation.Shapes.Add(bigWreckingBall);
            {
                //Everything identical to the first rope, but now with a heavier wrecking ball.
                //This is going to behave very poorly. The extremely heavy wrecking ball depends on the much lighter rope bodies.
                //The force required to keep the wrecking ball in place needs to propagate through the rope to the kinematic at the top, but the low mass of the rope 
                //makes that a very time consuming process. To make this work robustly with no changes to the constraint configuration, 
                //you would need to drop the time step duration and possibly increase the solver iteration count.
                //Neither option is great in a performance sensitive game.
                //(480hz update rate makes this pretty much rock solid. 240hz is usually okay, 120hz is iffy.
                //Changing solver iteration count alone isn't practically sufficient because of the stiffness-induced oscillations. Even going up to 5000 iterations isn't good enough at 60hz.)
                var startLocation = new Vector3(-35, 35, 0);
                const float bodySpacing = 0.3f;
                const float bodyRadius = 0.5f;
                var springSettings = new SpringSettings(30, 1);
                var bodyHandles = BuildRope(startLocation, 12, bodyRadius, bodySpacing, bodyRadius, 1, 1, springSettings);

                AttachWreckingBall(bodyHandles, bodyRadius, bodySpacing, bodyRadius, bigWreckingBall.Radius, bigWreckingBallInertia, bigWreckingBallIndex, springSettings);
                rolloverInfo.Add(startLocation + new Vector3(0, 2, 0), "Naive, 100:1 mass ratio");
            }
            {
                //If stiffness is causing a problem, how about we reduce the stiffness by adjusting the spring settings frequency? 
                //This certainly makes it more stable, but it behaves more like loose elastic than a rope.
                //If you have a simulation where softness is actually okay, this is often the quickest and easiest fix.
                var startLocation = new Vector3(-15, 35, 0);
                const float bodySpacing = 0.3f;
                const float bodyRadius = 0.5f;
                var springSettings = new SpringSettings(3, 1);
                var bodyHandles = BuildRope(startLocation, 12, bodyRadius, bodySpacing, bodyRadius, 1, 1, springSettings);

                AttachWreckingBall(bodyHandles, bodyRadius, bodySpacing, bodyRadius, bigWreckingBall.Radius, bigWreckingBallInertia, bigWreckingBallIndex, springSettings);
                rolloverInfo.Add(startLocation + new Vector3(0, 2, 0), "Softer constraints");
            }
            {
                //If the mass ratio between the wrecking ball and rope bodies make it hard to propagate impulses, how about increasing the mass of the rope?
                //It does help, but now the rope is really heavy. That's not really the behavior we want, and it's still not perfect.
                var startLocation = new Vector3(-5, 35, 0);
                const float bodySpacing = 0.3f;
                const float bodyRadius = 0.5f;
                var springSettings = new SpringSettings(30, 1);
                var bodyHandles = BuildRope(startLocation, 12, bodyRadius, bodySpacing, bodyRadius, 20, 1, springSettings);

                AttachWreckingBall(bodyHandles, bodyRadius, bodySpacing, bodyRadius, bigWreckingBall.Radius, bigWreckingBallInertia, bigWreckingBallIndex, springSettings);
                rolloverInfo.Add(startLocation + new Vector3(0, 2, 0), "20x rope mass boost");
            }
            {
                //The difficulty of propagating impulses isn't just a matter of mass alone. The constraint lever arms and body inertia tensors are also very important.
                //How about we treat the bodies as having way more rotational inertia than their shape would imply, and don't increase the mass as much?
                //This helps about as much as using the higher mass without quite as much negative impact on behavior.
                var startLocation = new Vector3(5, 35, 0);
                const float bodySpacing = 0.3f;
                const float bodyRadius = 0.5f;
                var springSettings = new SpringSettings(30, 1);
                var bodyHandles = BuildRope(startLocation, 12, bodyRadius, bodySpacing, bodyRadius, 5, 0.2f, springSettings);

                AttachWreckingBall(bodyHandles, bodyRadius, bodySpacing, bodyRadius, bigWreckingBall.Radius, bigWreckingBallInertia, bigWreckingBallIndex, springSettings);
                rolloverInfo.Add(startLocation + new Vector3(0, 2, 0), "5x rope mass boost, 25x rope inertia boost");
            }
            {
                //If increasing the inertia helped, and longer lever arms can make things trickier to solve, how about reducing the constraint lever arms to zero 
                //so that rope body angular motion doesn't matter at all? We'll also keep the mass at 5x higher than the naive version.
                //(We'll also lock the rope body orientation by setting their rotational inertia to infinity, but since the constraint lever arms are 0 length, that doesn't actually
                //change anything.)
                //This helps a lot. The angular oscillation is completely eliminated, and it takes quite a bit to force linear oscillation.
                var startLocation = new Vector3(15, 35, 0);
                const float bodySpacing = 0.3f;
                const float bodyRadius = 0.5f;
                var springSettings = new SpringSettings(30, 1);
                var bodyHandles = BuildRope(startLocation, 12, bodyRadius, bodySpacing, 0, 1, 0, springSettings);

                AttachWreckingBall(bodyHandles, bodyRadius, bodySpacing, 0, bigWreckingBall.Radius, bigWreckingBallInertia, bigWreckingBallIndex, springSettings);
                rolloverInfo.Add(startLocation + new Vector3(0, 2, 0), "5x rope mass boost, 0 lever arm");
            }
            {
                //But what if we don't want to change the mass of the rope bodies?
                //There's still an option! Attach the wrecking ball directly to the kinematic. No more concern about impulse propagation.
                //This makes the rest of the rope effectively cosmetic when the wrecking ball is freely swinging.
                //The problem is that you can *tell* that the rope is cosmetic. It's not loaded, so it flops around too much.
                //Further, if the rope wraps around something such that the cheat constraint isn't holding the weight anymore, the rope will freak out just as bad as it did in the first attempt.
                var startLocation = new Vector3(25, 35, 0);
                const float bodySpacing = 0.3f;
                const float bodyRadius = 0.5f;
                var springSettings = new SpringSettings(30, 1);
                var bodyHandles = BuildRope(startLocation, 12, bodyRadius, bodySpacing, 0, 1f, 0, springSettings);

                var wreckingBallHandle = AttachWreckingBall(bodyHandles, bodyRadius, bodySpacing, 0, bigWreckingBall.Radius, bigWreckingBallInertia, bigWreckingBallIndex, springSettings);
                var wreckingBallConnectionOffset = new Vector3(0, bigWreckingBall.Radius, 0);
                var maximumDistance = Vector3.Distance(
                    new BodyReference(bodyHandles[0], Simulation.Bodies).Pose.Position,
                    new BodyReference(wreckingBallHandle, Simulation.Bodies).Pose.Position + wreckingBallConnectionOffset);
                Simulation.Solver.Add(bodyHandles[0], wreckingBallHandle, new DistanceLimit(default, wreckingBallConnectionOffset, 0.01f, maximumDistance, springSettings));
                rolloverInfo.Add(startLocation + new Vector3(0, 2, 0), "100:1 mass ratio, direct cheat constraint");
            }
            {
                //The last attempt was pretty stable, but how do we address its problems? Instead of having a single constraint from the source to the target, we can
                //create a bunch of 'skip constraints' between nearby rope bodies. Rather than having to traverse the constraint graph one body a time,
                //these skip constraints allow impulses to propagate through the graph along multiple 'shortcut' paths at the same time. Any one body acting as a bottleneck
                //has its load propagated quickly to all its neighbors.
                //As a result, this is extremely stable. You can choose to increase the number of skip constraints for additional stability. Reducing the rope mass further is very possible.
                var startLocation = new Vector3(35, 140, 0);
                const float bodySpacing = 0.3f;
                const float bodyRadius = 0.5f;
                var springSettings = new SpringSettings(30, 1);
                var bodyHandles = BuildRopeBodies(startLocation, 100, bodyRadius, bodySpacing, 1f, 0);

                bool TryCreateConstraint(int handleIndexA, int handleIndexB)
                {
                    if (handleIndexA >= bodyHandles.Length || handleIndexB >= bodyHandles.Length)
                        return false;
                    var maximumDistance = Vector3.Distance(
                        new BodyReference(bodyHandles[handleIndexA], Simulation.Bodies).Pose.Position,
                        new BodyReference(bodyHandles[handleIndexB], Simulation.Bodies).Pose.Position);
                    Simulation.Solver.Add(bodyHandles[handleIndexA], bodyHandles[handleIndexB], new DistanceLimit(default, default, .01f, maximumDistance, springSettings));
                    return true;
                }
                const int constraintsPerBody = 4;
                for (int i = 0; i < bodyHandles.Length - 1; ++i)
                {
                    //Note that you could also create constraints which span even more links. For example, connect i and i+1, i+2, i+4, i+8 and i+16 rather than just the nearest bodies.
                    //That would make it behave a bit more like the previous cheat constraint, but it can be useful.
                    for (int j = 1; j <= constraintsPerBody; ++j)
                    {
                        if (!TryCreateConstraint(i, i + j))
                            break;
                    }
                }

                var wreckingBallHandle = CreateWreckingBall(bodyHandles, bodyRadius, bodySpacing, bigWreckingBall.Radius, bigWreckingBallInertia, bigWreckingBallIndex);
                var wreckingBallConnectionOffset = new Vector3(0, bigWreckingBall.Radius, 0);
                for (int i = 1; i <= constraintsPerBody; ++i)
                {
                    var targetBodyHandleIndex = bodyHandles.Length - i;
                    if (targetBodyHandleIndex < 0)
                        break;
                    var maximumDistance = Vector3.Distance(
                        new BodyReference(bodyHandles[targetBodyHandleIndex], Simulation.Bodies).Pose.Position,
                        new BodyReference(wreckingBallHandle, Simulation.Bodies).Pose.Position + wreckingBallConnectionOffset);
                    Simulation.Solver.Add(bodyHandles[targetBodyHandleIndex], wreckingBallHandle, new DistanceLimit(default, wreckingBallConnectionOffset, 0.01f, maximumDistance, springSettings));
                }
                rolloverInfo.Add(startLocation, $"100:1 mass ratio, {constraintsPerBody - 1}x extra skip constraints");
            }

            Simulation.Statics.Add(new StaticDescription(new Vector3(0, 0, 0), new CollidableDescription(Simulation.Shapes.Add(new Box(200, 1, 200)), 0.1f)));
            Simulation.Statics.Add(new StaticDescription(
                new Vector3(100, 70, 0), BepuUtilities.Quaternion.CreateFromAxisAngle(new Vector3(1, 0, 0), MathF.PI * 0.5f),
                new CollidableDescription(Simulation.Shapes.Add(new Capsule(8, 64)), 0.1f)));

        }

        public override void Render(Renderer renderer, Camera camera, Input input, TextBuilder text, Font font)
        {
            rolloverInfo.Render(renderer, camera, input, text, font);   
            base.Render(renderer, camera, input, text, font);
        }

    }
}