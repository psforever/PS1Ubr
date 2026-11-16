using System;
using System.Globalization;

namespace PS1Ubr
{
    /// <summary>
    /// The client converts the decimal text of its .lst scripts to float by truncating toward zero (it never rounds up to the nearest float), so a
    /// coordinate such as 897.21 becomes 897.20996 where the nearest float is 897.21002. This matters when ordering objects whose coordinates differ by about 0.1.
    /// Confirmed against client dumps (every cave groundcover coordinate); see the client GUID validation notes.
    /// </summary>
    public static class ClientFloat
    {
        public static float Parse(string text)
        {
            var d = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            var f = (float)d;
            if (Math.Abs((double)f) > Math.Abs(d))
            {
                // one step toward zero: the magnitude of an IEEE float is its bit pattern minus its sign bit, so subtract one from the pattern
                f = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(f) - 1);
            }
            return f;
        }
    }
}
