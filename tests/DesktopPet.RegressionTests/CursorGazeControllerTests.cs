using DesktopPet.Core;

public static class CursorGazeControllerTests
{
    public static IEnumerable<(string Name, Action Run)> Create()
    {
        yield return ("Cursor jitter across the center retains the previous gaze in either direction", () =>
        {
            var gaze = new CursorGazeController();
            foreach (var initial in new[] { true, false })
            {
                gaze.Reset(initial);
                for (var cycle = 0; cycle < 100; cycle++)
                foreach (var offset in new[] { -7.9, 0, 7.9, -0.01, 0.01, 0 })
                    Check(gaze.Update(100 + offset, 100) == initial && gaze.FacingRight == initial,
                        "Pointer noise inside the center band flipped the gaze");
            }
        });

        yield return ("Gaze changes only after strictly crossing the opposite dead-zone edge", () =>
        {
            var gaze = new CursorGazeController();
            Check(gaze.Update(92, 100), "Exact left boundary changed the retained right pose");
            Check(!gaze.Update(91.99, 100), "Crossing the left boundary failed to turn left");
            Check(!gaze.Update(100, 100) && !gaze.Update(108, 100), "Center or exact right boundary changed the retained left pose");
            Check(gaze.Update(108.01, 100), "Crossing the right boundary failed to turn right");
            Check(gaze.Update(92, 100), "Returning to the left boundary immediately flipped back");
        });

        yield return ("An intentional far-side cursor movement turns immediately without a dwell delay", () =>
        {
            var gaze = new CursorGazeController();
            Check(!gaze.Update(-10000, 200), "A deliberate far-left movement was delayed");
            Check(gaze.Update(10000, 200), "A deliberate far-right movement was delayed");
            Check(!gaze.Update(-10000, 200), "A second intentional reversal was suppressed");
        });

        yield return ("Gaze reset explicitly selects left and defaults back to right", () =>
        {
            var gaze = new CursorGazeController();
            Check(gaze.FacingRight, "Initial gaze is not right-facing");
            gaze.Reset(false);
            Check(!gaze.FacingRight && !gaze.Update(300, 300), "Left reset was lost on a centered sample");
            gaze.Reset();
            Check(gaze.FacingRight && gaze.Update(300, 300), "Default reset did not clear the previous left pose");
        });

        yield return ("Negative monitor positions use the same gaze boundaries as positive screens", () =>
        {
            foreach (var center in new[] { -3840d, -1920, -100, 0, 1920 })
            {
                var gaze = new CursorGazeController();
                Check(!gaze.Update(center - 9, center), "Negative-screen left crossing did not turn left");
                Check(!gaze.Update(center + 7, center), "Negative-screen center band failed to hold left");
                Check(gaze.Update(center + 9, center), "Negative-screen right crossing did not turn right");
            }
        });

        yield return ("Consistently scaled cursor coordinates and bands preserve gaze decisions", () =>
        {
            foreach (var scale in new[] { 0.5, 1, 1.25, 2.5 })
            {
                var gaze = new CursorGazeController();
                var offsets = new[] { -9d, -3, 3, 8, 9, 1, -1, -8, -9 };
                var expected = new[] { false, false, false, false, true, true, true, true, false };
                for (var i = 0; i < offsets.Length; i++)
                    Check(gaze.Update((-500 + offsets[i]) * scale, -500 * scale, 8 * scale) == expected[i],
                        "Changing character scale changed a normalized gaze decision");
            }
        });

        yield return ("Invalid coordinates and invalid bands preserve both left and right gaze", () =>
        {
            var gaze = new CursorGazeController();
            foreach (var initial in new[] { true, false })
            {
                gaze.Reset(initial);
                foreach (var invalid in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
                {
                    Check(gaze.Update(invalid, 0) == initial, "Invalid cursor changed gaze");
                    Check(gaze.Update(initial ? -100 : 100, invalid) == initial, "Invalid character center changed gaze");
                    Check(gaze.Update(initial ? -100 : 100, 0, invalid) == initial, "Invalid band changed gaze");
                }
                Check(gaze.Update(initial ? -100 : 100, 0, -1) == initial && gaze.FacingRight == initial,
                    "Negative dead zone changed gaze");
            }
        });

        yield return ("A zero-width gaze band turns by cursor side and holds at the exact center", () =>
        {
            var gaze = new CursorGazeController();
            Check(!gaze.Update(-0.01, 0, 0), "Zero-width band failed to turn left");
            Check(!gaze.Update(0, 0, 0), "Exact center discarded the retained left gaze");
            Check(gaze.Update(0.01, 0, 0), "Zero-width band failed to turn right");
            Check(gaze.Update(0, 0, 0), "Exact center discarded the retained right gaze");
        });

        yield return ("Finite extreme coordinates still identify a deliberate opposite-side gaze", () =>
        {
            var gaze = new CursorGazeController();
            Check(!gaze.Update(-double.MaxValue, double.MaxValue, double.MaxValue), "Finite far-left difference overflow lost the turn");
            Check(gaze.Update(double.MaxValue, -double.MaxValue, double.MaxValue), "Finite far-right difference overflow lost the turn");
            Check(gaze.Update(0, double.MaxValue, double.MaxValue), "An exact extreme boundary changed the retained pose");
        });

        yield return ("A character moving past a stationary pointer changes gaze once beyond the band", () =>
        {
            var gaze = new CursorGazeController();
            var changes = 0;
            var previous = gaze.FacingRight;
            for (var center = -16d; center <= 16; center += 0.25)
            {
                var direction = gaze.Update(0, center);
                if (direction != previous) changes++;
                previous = direction;
                if (center <= 8) Check(direction, "Moving character flipped before crossing its dead zone");
                else Check(!direction, "Moving character failed to face the stationary pointer");
            }
            Check(changes == 1, "Moving across the stationary pointer produced repeated gaze flips");
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
