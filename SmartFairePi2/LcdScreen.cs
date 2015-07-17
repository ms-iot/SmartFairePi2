using System;
using Windows.Devices.I2c;

namespace SmartFairePi2
{
    class LcdScreen
    {
        private const byte ENABLE_BIT = 0x20;
        private const byte ENABLE_BIT_INVERSE = 0xDF;
        private const byte READ_BIT = 0x40;
        private const byte DATA_BIT = 0x80;
        private const byte WRITE_INSTRUCTION = 0x0;
        private const byte WRITE_DATA = DATA_BIT;

        private I2cDevice i2cLcdScreen;
        public LcdScreen(I2cDevice screen)
        {
            i2cLcdScreen = screen;
        }

        public static void Delay(int ms)
        {
            int time = Environment.TickCount;
            while (true)
                if (Environment.TickCount - time >= ms)
                    return;
        }
        public void LcdReset()
        {
            Send4BitInstruction(0x03);
            Delay(4);
            Send4BitInstruction(0x03);
            Send4BitInstruction(0x03);
            // Function set (Set interface to be 4 bits long.) Interface is 8 bits in length.
            Send4BitInstruction(0x02);

            // Set number of lines and font
            Send4BitInstruction(0x02);
            Send4BitInstruction(0x08);
            // Turn on display.  No cursor
            Send4BitInstruction(0x00);
            Send4BitInstruction(0x0C);

            // Entry mode set - increment addr by 1, shift cursor by right.
            Send4BitInstruction(0x00);
            Send4BitInstruction(0x06);

            LcdClear();
        }
        public void LcdClear()
        {
            Send4BitInstruction(0x00);
            Send4BitInstruction(0x01);
            Delay(1);
            // return to zero
            Send4BitInstruction(0x00);
            Send4BitInstruction(0x02);
            Delay(1);
        }
        public void MoveToLine(byte line)
        {
            switch (line)
            {
                case 0:
                    SetDisplayAddress(0);
                    break;
                case 1:
                    SetDisplayAddress(64);
                    break;
                case 2:
                    SetDisplayAddress(20);
                    break;
                case 3:
                    SetDisplayAddress(84);
                    break;
            }
        }
        public void PrintLine(string data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                Send8BitCharacter((byte)data.ToCharArray()[i]);
                Delay(1);
            }
        }
        private void Send4BitInstruction(byte c)
        {
            Send4Bits(WRITE_INSTRUCTION, c);
        }
        private void Send8BitCharacter(byte c)
        {
            byte temp = c;
            temp >>= 4;
            Send4Bits(WRITE_DATA, temp);
            temp = c;
            temp &= 0x0F;
            Send4Bits(WRITE_DATA, temp);
        }
        private void Send4Bits(byte control, byte d)
        {
            byte portB = control;
            portB |= FlipAndShift(d);
            portB |= ENABLE_BIT;
            i2cLcdScreen.Write(new byte[] { Mcp23017.GPIOB, portB });

            portB &= 0xDF;
            i2cLcdScreen.Write(new byte[] { Mcp23017.GPIOB, portB });
        }
        private void SetDisplayAddress(byte addr)
        {
            byte temp = addr;
            temp >>= 4;
            temp |= 0x08;
            Send4BitInstruction(temp);
            temp = addr;
            temp &= 0x0F;
            Send4BitInstruction(temp);
        }
        private byte FlipAndShift(byte src)
        {
            byte dest = 0;
            if ((src & 0x1) != 0)
            {
                dest |= 0x10;
            }
            if ((src & 0x2) != 0)
            {
                dest |= 0x08;
            }
            if ((src & 0x4) != 0)
            {
                dest |= 0x04;
            }
            if ((src & 0x8) != 0)
            {
                dest |= 0x02;
            }
            return dest;
        }
    }
}
