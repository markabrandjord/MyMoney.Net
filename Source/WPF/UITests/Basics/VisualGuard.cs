using System;
using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using NUnit.Framework;

namespace Walkabout.UITests.Basics
{
    /// <summary>
    /// Self-referential visual invariants that catch purely-visual/timing bugs FlaUI's
    /// automation-tree assertions (AutomationId/ControlType/pattern state) cannot see - added
    /// after Phase 1 shipped a green FlaUI suite alongside four real visual bugs (a truncated
    /// menu label, a wrong-colored dialog under Dark theme, a blank title bar, and an animation
    /// racing the app's own layout code). No golden-image baselines: each check is an invariant
    /// about the element's own captured pixels, so it needs no stored reference image and no
    /// tolerance tuning for theme/DPI/frame variance.
    /// </summary>
    internal static class VisualGuard
    {
        /// <summary>
        /// Fails if the element's right-edge margin (background-colored space between its
        /// content and its right boundary) is dramatically smaller than its left-edge margin -
        /// the signature of text/content clipped by a container too small for its content (e.g.
        /// issue: MainWindow's Help menu item truncated to "He"). Comparing left vs. right margin
        /// (rather than just checking "is anything near the edge") is deliberate: a control whose
        /// bounding box is tightly content-sized with little padding on either side (e.g. a
        /// MenuItem's own label rect) is normal, not clipped, and a naive "any foreground pixel
        /// near the edge" check false-positives on it - confirmed live, 2026-09-20, against
        /// MainWindow's correctly-rendering "Help" label before this asymmetry check replaced it.
        /// </summary>
        internal static void AssertNotClipped(AutomationElement element)
        {
            using Bitmap bmp = Capture.Element(element).Bitmap;
            if (bmp.Width < 10 || bmp.Height < 4)
            {
                return; // too small to meaningfully measure margins; not this guard's job
            }

            Color background = bmp.GetPixel(1, 1); // corner pixel, assumed representative of background
            const int colorTolerance = 12; // small AA/subpixel tolerance, not a golden-image threshold
            int midY = bmp.Height / 2;

            bool IsBackground(int x, int y)
            {
                Color c = bmp.GetPixel(x, y);
                return Math.Abs(c.R - background.R) <= colorTolerance &&
                       Math.Abs(c.G - background.G) <= colorTolerance &&
                       Math.Abs(c.B - background.B) <= colorTolerance;
            }

            int MeasureMargin(bool fromLeft)
            {
                int margin = 0;
                for (int i = 0; i < bmp.Width; i++)
                {
                    int x = fromLeft ? i : bmp.Width - 1 - i;
                    if (!IsBackground(x, midY))
                    {
                        break;
                    }
                    margin++;
                }
                return margin;
            }

            int leftMargin = MeasureMargin(fromLeft: true);
            int rightMargin = MeasureMargin(fromLeft: false);

            // Only meaningful if the element actually has a real left margin to compare against -
            // an element with near-zero margin on BOTH sides is tightly content-sized by design,
            // not evidence of anything. Require a real left margin (>= 4px) before judging the
            // right side suspicious, and require the right margin to be both small in absolute
            // terms (< 2px) and clearly smaller than the left (less than half) - two independent
            // signals, not just a ratio that could trip on naturally-asymmetric content.
            if (leftMargin >= 4 && rightMargin < 2 && rightMargin < leftMargin / 2)
            {
                Assert.Fail($"VisualGuard.AssertNotClipped: '{element.Name}' has a {leftMargin}px " +
                    $"left margin but only a {rightMargin}px right margin - likely truncated content " +
                    "(asymmetric margins are the clipping signature; symmetric tight margins are normal).");
            }
        }

        /// <summary>
        /// Fails if the element's captured area is a single flat color (a blank/broken-render
        /// region), or if its mean luminance doesn't match the expected theme - catches e.g. a
        /// title bar rendering with no text, or a dialog with the wrong-theme background.
        /// </summary>
        internal static void AssertNotBlank(AutomationElement element, bool expectDark)
        {
            using Bitmap bmp = Capture.Element(element).Bitmap;
            var distinctColors = new System.Collections.Generic.HashSet<int>();
            long luminanceSum = 0;
            int sampleCount = 0;

            for (int x = 0; x < bmp.Width; x += Math.Max(1, bmp.Width / 40))
            {
                for (int y = 0; y < bmp.Height; y += Math.Max(1, bmp.Height / 40))
                {
                    Color c = bmp.GetPixel(x, y);
                    distinctColors.Add(c.ToArgb());
                    luminanceSum += (c.R + c.G + c.B) / 3;
                    sampleCount++;
                }
            }

            if (distinctColors.Count <= 1)
            {
                Assert.Fail($"VisualGuard.AssertNotBlank: '{element.Name}' is a single flat color - likely a blank/broken render.");
            }

            double meanLuminance = luminanceSum / (double)sampleCount / 255.0;
            if (expectDark && meanLuminance > 0.5)
            {
                Assert.Fail($"VisualGuard.AssertNotBlank: '{element.Name}' has mean luminance " +
                    $"{meanLuminance:F2} (expected dark, < 0.5) - likely still rendering Light-themed.");
            }
            if (!expectDark && meanLuminance < 0.2)
            {
                Assert.Fail($"VisualGuard.AssertNotBlank: '{element.Name}' has mean luminance " +
                    $"{meanLuminance:F2} (expected light, > 0.2) - likely rendering as a dark/blank box.");
            }
        }

        /// <summary>
        /// Runs <paramref name="interaction"/>, captures immediately and again after
        /// <paramref name="budget"/>, and fails if the two captures differ - meaning the element
        /// was still visually changing (an in-progress animation/layout pass) past the expected
        /// settle time. No baseline needed: this compares the element only against itself.
        /// </summary>
        internal static void AssertSettledWithin(AutomationElement element, TimeSpan budget, Action interaction)
        {
            interaction();
            using Bitmap immediate = Capture.Element(element).Bitmap;
            System.Threading.Thread.Sleep(budget);
            using Bitmap settled = Capture.Element(element).Bitmap;

            if (immediate.Width != settled.Width || immediate.Height != settled.Height)
            {
                return; // size itself changed (e.g. real expand/collapse) - not this guard's concern
            }

            int sampleStepX = Math.Max(1, immediate.Width / 20);
            int sampleStepY = Math.Max(1, immediate.Height / 20);
            for (int x = 0; x < immediate.Width; x += sampleStepX)
            {
                for (int y = 0; y < immediate.Height; y += sampleStepY)
                {
                    if (immediate.GetPixel(x, y).ToArgb() != settled.GetPixel(x, y).ToArgb())
                    {
                        Assert.Fail($"VisualGuard.AssertSettledWithin: '{element.Name}' was still " +
                            $"visually changing {budget.TotalMilliseconds}ms after the interaction - " +
                            "a repaint/animation is still in progress past the expected settle time.");
                    }
                }
            }
        }
    }
}
