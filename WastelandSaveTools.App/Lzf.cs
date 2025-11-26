using System;

namespace WastelandSaveTools.App
{
    /// <summary>
    /// LZF decompression implementation.
    /// </summary>
    public static class Lzf
    {
        /// <summary>
        /// Decompresses an LZF-compressed buffer into the given output buffer.
        /// Returns the number of bytes written to <paramref name="output"/>,
        /// or 0 if decompression failed.
        /// </summary>
        public static int Decompress(byte[] input, int inputLength, byte[] output, int outputLength)
        {
            var inPtr = 0;
            var outPtr = 0;

            while (inPtr < inputLength && outPtr < outputLength)
            {
                int ctrl = input[inPtr++];

                if (ctrl < 32)
                {
                    // Literal run
                    ctrl += 1;
                    if (outPtr + ctrl > outputLength || inPtr + ctrl > inputLength)
                    {
                        return 0;
                    }

                    Array.Copy(input, inPtr, output, outPtr, ctrl);
                    inPtr += ctrl;
                    outPtr += ctrl;
                }
                else
                {
                    // Back-reference
                    var len = ctrl >> 5;
                    var refOffset = outPtr - ((ctrl & 0x1F) << 8) - 1;

                    if (inPtr >= inputLength)
                    {
                        return 0;
                    }

                    if (len == 7)
                    {
                        if (inPtr >= inputLength)
                        {
                            return 0;
                        }

                        len += input[inPtr++];
                    }

                    if (inPtr >= inputLength)
                    {
                        return 0;
                    }

                    refOffset -= input[inPtr++];

                    if (refOffset < 0 || outPtr + len + 2 > outputLength)
                    {
                        return 0;
                    }

                    for (var i = 0; i < len + 2; i++)
                    {
                        output[outPtr++] = output[refOffset + i];
                    }
                }
            }

            return outPtr;
        }
    }
}
