using System;

namespace SmartFairePi2
{
    class Mcp23017
    {
        //MCP23017 Registers
        public const byte IODIRA = 0x00; //Register for input/output mode for port A
        public const byte IODIRB = 0x01; //Register for input/output mode for port B
        public const byte IPOLA = 0x02; //Register for invert mode for port A
        public const byte IPOLB = 0x03; //Register for invert mode for port B
        public const byte GPPUA = 0x0C; //Register to set port A as pull-up
        public const byte GPPUB = 0x0D; //Register to set port B as pull-up
        public const byte GPIOA = 0x12; //Register to read and write values to port A
        public const byte GPIOB = 0x13; //Register to read and write values to port B
    }
}
