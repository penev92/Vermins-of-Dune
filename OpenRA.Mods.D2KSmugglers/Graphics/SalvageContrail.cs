#region Copyright & License Information
/*
 * Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Effects;
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Mods.D2KSmugglers.Graphics
{
	public struct SalvageContrailRenderable : IRenderable, IFinalizedRenderable
	{
		public int Length { get { return trail.Length; } }

		readonly World world;
		readonly Color color;
		readonly int zOffset;

		// Store trail positions in a circular buffer
		readonly WPos[] trail;
		readonly WDist width;
		int next;
		int length;
		readonly int skip;

		public SalvageContrailRenderable(World world, Color color, WDist width, int length, int skip, int zOffset)
			: this(world, new WPos[length], width, 0, 0, skip, color, zOffset) { }

		SalvageContrailRenderable(World world, WPos[] trail, WDist width, int next, int length, int skip, Color color, int zOffset)
		{
			this.world = world;
			this.trail = trail;
			this.width = width;
			this.next = next;
			this.length = length;
			this.skip = skip;
			this.color = color;
			this.zOffset = zOffset;
		}

		public WPos Pos { get { return trail[Index(next - 1)]; } }
		public PaletteReference Palette { get { return null; } }
		public int ZOffset { get { return zOffset; } }
		public bool IsDecoration { get { return true; } }

		public IRenderable WithPalette(PaletteReference newPalette) { return new SalvageContrailRenderable(world, (WPos[])trail.Clone(), width, next, length, skip, color, zOffset); }
		public IRenderable WithZOffset(int newOffset) { return new SalvageContrailRenderable(world, (WPos[])trail.Clone(), width, next, length, skip, color, newOffset); }
		public IRenderable OffsetBy(in WVec vec)
		{
			// Lambdas can't use 'in' variables, so capture a copy for later
			var offset = vec;
			return new SalvageContrailRenderable(world, trail.Select(pos => pos + offset).ToArray(), width, next, length, skip, color, zOffset);
		}

		public IRenderable AsDecoration() { return this; }

		public IFinalizedRenderable PrepareRender(WorldRenderer wr) { return this; }

		public static float PositionBasedRadiusModifier(float time_in_seconds, WPos position)
		{
			var mutliplier = (
				(1.0 + 0.3 * Math.Sin((double)position.X * 100.0 + 10.0 * time_in_seconds)) *
				(1.0 + 0.3 * Math.Sin((double)position.Y * 100.0 + 10.0 * time_in_seconds)) *
				(1.0 + 0.3 * Math.Sin((double)position.Z * 100.0 + 10.0 * time_in_seconds)));

			return (float)mutliplier;
		}

		public Color GetParameterizedColor(float t)
		{
			if (t < 0.5)
				return Exts.ColorLerp(2 * t, Color.FromArgb(0x00ff0000), Color.FromArgb(0x22000000));
			else if (t < 0.75)
				return Exts.ColorLerp(4 * t - 2, Color.FromArgb(0x22000000), Color.FromArgb(0x88000000));
			else
				return Exts.ColorLerp(4 * t - 3, Color.FromArgb(0x88000000), Color.FromArgb(0x00000000));
		}

		public void Render(WorldRenderer wr)
		{
			// Need at least 4 points to smooth the contrail over
			if (length - skip < 4)
				return;
			var screenWidth = wr.ScreenVector(new WVec(width, WDist.Zero, WDist.Zero))[0];
			var wcr = Game.Renderer.WorldRgbaColorRenderer;

			// Start of the first line segment is the tail of the list - don't smooth it.
			var curPos = trail[Index(next - skip - 1)];
			var curColor = Color.FromArgb(0x00ff0000);
			for (var i = 0; i < length - skip - 4; i++)
			{
				var j = next - skip - i - 2;
				var nextPos = Average(trail[Index(j)], trail[Index(j - 1)], trail[Index(j - 2)], trail[Index(j - 3)]);
				var nextColor = GetParameterizedColor(i * 1f / (length - 4));

				if (!world.FogObscures(curPos) && !world.FogObscures(nextPos))
				{
					var time_in_seconds = (float)wr.World.Timestep / 30;
					var modifier = PositionBasedRadiusModifier(time_in_seconds, curPos);
					wcr.DrawLine(wr.Screen3DPosition(curPos), wr.Screen3DPosition(nextPos), modifier * screenWidth, curColor, nextColor);
				}

				curPos = nextPos;
				curColor = nextColor;
			}
		}

		public void RenderDebugGeometry(WorldRenderer wr) { }
		public Rectangle ScreenBounds(WorldRenderer wr) { return Rectangle.Empty; }

		// Array index modulo length
		int Index(int i)
		{
			var j = i % trail.Length;
			return j < 0 ? j + trail.Length : j;
		}

		public int GetTailLength()
		{
			return (Pos - trail[Index(next - length + 1)]).Length;
		}

		static WPos Average(params WPos[] list)
		{
			return list.Average();
		}

		public void Update(WPos pos)
		{
			trail[next] = pos;
			next = Index(next + 1);

			if (length < trail.Length)
				length++;
		}

		public static Color ChooseColor(Actor self)
		{
			var ownerColor = Color.FromArgb(255, self.Owner.Color);
			return Exts.ColorLerp(0.5f, ownerColor, Color.White);
		}
	}

	public class SalvageContrailFader : IEffect
	{
		readonly WPos pos;
		SalvageContrailRenderable trail;
		int ticks;

		public SalvageContrailFader(WPos pos, SalvageContrailRenderable trail)
		{
			this.pos = pos;
			this.trail = trail;
		}

		public void Tick(World world)
		{
			if (ticks++ == trail.Length)
				world.AddFrameEndTask(w => w.Remove(this));

			trail.Update(pos);
		}

		public IEnumerable<IRenderable> Render(WorldRenderer wr)
		{
			yield return trail;
		}
	}
}
