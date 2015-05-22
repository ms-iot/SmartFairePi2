using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Devices.Enumeration;
using Windows.Devices.I2c;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace SmartFairePi2
{
    public sealed partial class MainPage : Page
    {
        /**************************************************************************************************************
        * Private 
        **************************************************************************************************************/
        // use these constants for controlling how the I2C bus is setup
        private const string I2C_CONTROLLER_NAME = "I2C1"; //Specific to RPI2
        private const byte BUTTON_PANEL_I2C_ADDRESS = 0x20; //Address of the port expander controlling the button panel
        private const byte LCD_SCREEN_I2C_ADDRESS = 0x21; //Address of the port expander controlling the LCD screen

        private const double BUTTON_STATUS_CHECK_TIMER_INTERVAL = 50; //Timer interval for checking buttons on idle state
        private const double GAME_TIMER_INTERVAL = 50; //Timer interval during gameplay

        private I2cDevice i2cButtonPanel; //Port expander controlling the button panel
        private I2cDevice i2cLcdScreen; //Port expander controlling the LCD screen
        private LcdScreen Screen; //Lcd Screen class
        private DispatcherTimer buttonStatusCheckTimer; //Timer controlling the idle state

        private const byte BUTTON_1 = 0x01;
        private const byte BUTTON_2 = 0x02;
        private const byte BUTTONS = BUTTON_1 | BUTTON_2;

        //Masks for random button lights. Only provide options that have 1 or 2 lights lit.  
        private static byte[] validMasks = { 0x01,
                                             0x02,
                                             0x03,
                                             0x04,
                                             0x05,
                                             0x06,
                                             0x08,
                                             0x09,
                                             0x0A,
                                             0x0C };

        /**************************************************************************************************************
        * Public 
        **************************************************************************************************************/
        public static uint p1Count = 0; //Player 1 points
        public static uint p2Count = 0; //Player 2 points
        public static byte p1Mask = 0; //Mask to light up random buttons for player 1
        public static byte p2Mask = 0; //Mask to light up random buttons for player 2
        public byte Player1Buttons = 0x0F; //Buttons for player 1
        public byte Player2Buttons = 0xF0; //Buttons for player 2
        public byte Player1Shift = 0; //Number of bits to shift when reading player 1
        public byte Player2Shift = 4; //Number of bits to shift when reading player 2
        public byte mask = 0x11; //Mask to light up button on random state

        public MainPage()
        {
            this.InitializeComponent();

            // Register for the unloaded event so we can clean up upon exit
            Unloaded += MainPage_Unloaded;

            InitializeSystem();
        }

        private async void InitializeSystem()
        {
            // initialize I2C communications
            try
            { 
                string deviceSelector = I2cDevice.GetDeviceSelector(I2C_CONTROLLER_NAME);
                var i2cDeviceControllers = await DeviceInformation.FindAllAsync(deviceSelector);

                //Port expander controlling the Button Panel
                var buttonPanelSettings = new I2cConnectionSettings(BUTTON_PANEL_I2C_ADDRESS);
                buttonPanelSettings.BusSpeed = I2cBusSpeed.FastMode;
                i2cButtonPanel = await I2cDevice.FromIdAsync(i2cDeviceControllers[0].Id, buttonPanelSettings);

                //Port expander controlling the LCD Screen
                var lcdSettings = new I2cConnectionSettings(LCD_SCREEN_I2C_ADDRESS);
                lcdSettings.BusSpeed = I2cBusSpeed.FastMode;
                i2cLcdScreen = await I2cDevice.FromIdAsync(i2cDeviceControllers[0].Id, lcdSettings);

                Screen = new LcdScreen(i2cLcdScreen);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Exception: {0}", e.Message);
                return;
            }

            // initialize I2C Port Expander registers
            try
            {
                InitializeButtonPanel();
                InitializeLcd();

            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Exception: {0}", e.Message);
                return;
            }

            try
            {
                buttonStatusCheckTimer = new DispatcherTimer();
                buttonStatusCheckTimer.Interval = TimeSpan.FromMilliseconds(BUTTON_STATUS_CHECK_TIMER_INTERVAL);
                buttonStatusCheckTimer.Tick += ButtonStatusCheckTimer_Tick;
                buttonStatusCheckTimer.Start();
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine("Exception: {0}", e.Message);
                return;
            }
        }
        private void InitializeButtonPanel()
        {
            i2cButtonPanel.Write(new byte[] { Mcp23017.IPOLA, 0xFF });
            i2cButtonPanel.Write(new byte[] { Mcp23017.GPPUA, 0xFF });
            i2cButtonPanel.Write(new byte[] { Mcp23017.IODIRA, 0xFF });

            i2cButtonPanel.Write(new byte[] { Mcp23017.IODIRB, 0x00 });
            i2cButtonPanel.Write(new byte[] { Mcp23017.GPIOB, 0x00 });
        }

        private void InitializeLcd()
        {
            i2cLcdScreen.Write(new byte[] { Mcp23017.IPOLA, 0x03 });
            i2cLcdScreen.Write(new byte[] { Mcp23017.GPPUA, 0x03 });
            i2cLcdScreen.Write(new byte[] { Mcp23017.IODIRA, 0x03 });

            i2cLcdScreen.Write(new byte[] { Mcp23017.IODIRB, 0x00 });
            i2cLcdScreen.Write(new byte[] { Mcp23017.GPIOB, 0x00 });
            Screen.LcdReset();
            PrintIdleText();
        }
        private void MainPage_Unloaded(object sender, object args)
        {
            /* Cleanup */
            i2cButtonPanel.Dispose();
            i2cLcdScreen.Dispose();
        }

        private void CheckButtonStatus()
        {
            byte[] readBuffer = new byte[1];
            i2cButtonPanel.WriteRead(new byte[] { Mcp23017.GPIOA }, readBuffer);
            if (readBuffer[0] != 0)
            {
                PlayTheGame();
                PrintIdleText();
            }
            else 
            {
                i2cLcdScreen.WriteRead(new byte[] { Mcp23017.GPIOA }, readBuffer);
                if (readBuffer[0] != 0)
                {
                    PlayTheGame();
                    PrintIdleText();
                }
                else
                {
                    mask = GetNextLight(mask);
                    i2cButtonPanel.Write(new byte[] { Mcp23017.GPIOB, GetNextLight(mask) });
                }
            }
        }

        private byte GetNextLight(byte mask)
        {
            if(mask == 0x88)
                mask = 0x11;
            else
                mask <<= 1;
            return mask;
        }

        private void ButtonStatusCheckTimer_Tick(object sender, object e)
        {
            CheckButtonStatus();
        }
        
        public static byte GetRandomButtonMask(byte oldMask)
        {
            Random random = new Random();
            while (true)
            {
                byte newMask = validMasks[random.Next(0, 9)];
                if (newMask != oldMask)
                    return newMask;
            }
        }

        void GameLoop()
        {
            byte[] readBuffer = new byte[1];
            // Get new masks if we need them.
            if (p1Mask == 0)
            {
                p1Mask = GetRandomButtonMask(p1Mask);
                i2cButtonPanel.WriteRead(new byte[] { Mcp23017.GPIOB }, readBuffer);
                readBuffer[0] &= Player2Buttons;
                readBuffer[0] |= p1Mask;
                i2cButtonPanel.Write(new byte[] { Mcp23017.GPIOB, readBuffer[0] });
            }
            if (p2Mask == 0)
            {
                p2Mask = GetRandomButtonMask(p2Mask);
                p2Mask <<= Player2Shift;
                i2cButtonPanel.WriteRead(new byte[] { Mcp23017.GPIOB }, readBuffer);
                readBuffer[0] &= Player1Buttons;
                readBuffer[0] |= p2Mask;
                i2cButtonPanel.Write(new byte[] { Mcp23017.GPIOB, readBuffer[0] });
            }

            // Check to see if we have hits.  If so, up the count and 
            // null the mask so we get a new one the next time around.
            i2cButtonPanel.WriteRead(new byte[] { Mcp23017.GPIOA }, readBuffer);
            if ((readBuffer[0] & Player1Buttons)== p1Mask)
            {
                p1Count++;
                p1Mask = 0;
            }
            if ((readBuffer[0] & Player2Buttons) == p2Mask)
            {
                p2Count++;
                p2Mask = 0;
            }
        }

        void PlayTheGame()
        {
            i2cButtonPanel.Write(new byte[] { Mcp23017.GPIOB, 0x00 });
            const uint maxTime = 20000;
            PrintSpeedInstructions();
            
            p1Count = 0;
            p2Count = 0;
            p1Mask = 0;
            p2Mask = 0;

            int startTime = Environment.TickCount;
            while ((Environment.TickCount - startTime) <= maxTime)
            {
                GameLoop();
            }
            FlashWinner();
            PrintGameOver();          
        }

        public int line = 0;

        public void FlashWinner()
        {
            if (p1Count > p2Count)
            {
                FlashButtons(Player1Buttons);
            }
            else if (p2Count > p1Count)
            {
                FlashButtons(Player2Buttons);
            }
            else 
            {
                FlashButtons(0xFF);
            }
        }

        private void FlashButtons(byte port)
        {
            i2cButtonPanel.Write(new byte[] { Mcp23017.GPIOB, 0x00 });
            for (int i = 0; i < 3; i++)
            {
                i2cButtonPanel.Write(new byte[] { Mcp23017.GPIOB, port });
                LcdScreen.Delay(400);
                i2cButtonPanel.Write(new byte[] { Mcp23017.GPIOB, 0x00 });
                LcdScreen.Delay(400);
            }
        }
        public void PrintIdleText()
        {
            Screen.LcdReset();
            Screen.MoveToLine(0x00);
            Screen.PrintLine("     Welcome to     ");
            Screen.MoveToLine(0x01);
            Screen.PrintLine("-----MakerFaire-----");
            Screen.MoveToLine(0x02);
            Screen.PrintLine("  Press any button  ");
            Screen.MoveToLine(0x03);
            Screen.PrintLine("  to begin a game!  ");
        }

        public void PrintSpeedInstructions()
        {
            Screen.LcdReset();
            Screen.MoveToLine(0x00);
            Screen.PrintLine(" Press all the lit  ");
            Screen.MoveToLine(0x01);
            Screen.PrintLine("  buttons together, ");
            Screen.MoveToLine(0x02);
            Screen.PrintLine("as fast as you can! ");
            Screen.MoveToLine(0x03);
            Screen.PrintLine("     GOOD LUCK!     ");
        }

        public void PrintGameOver()
        {
            Screen.LcdReset();
            Screen.MoveToLine(0x00);
            Screen.PrintLine("     GAME OVER!     ");
            if (p1Count == 0 || p2Count == 0)
            {
                Screen.MoveToLine(0x01);
                Screen.PrintLine("--------------------");
                Screen.MoveToLine(0x02);
                string temp = "Points = " + Convert.ToString(p1Count + p2Count);
                Screen.PrintLine(temp);
                Screen.MoveToLine(0x03);
                Screen.PrintLine("  Congratulations!  ");
            }
            else
            {
                Screen.MoveToLine(0x01);
                if (p1Count > p2Count)
                    Screen.PrintLine("   Player 1 wins!   ");
                else if (p2Count > p1Count)
                    Screen.PrintLine("   Player 2 wins!   ");
                else
                    Screen.PrintLine("    It is a tie!    ");
                Screen.MoveToLine(0x02);
                string temp = "Player 1 = " + Convert.ToString(p1Count);
                Screen.PrintLine(temp);
                Screen.MoveToLine(0x03);
                temp = "Player 2 = " + Convert.ToString(p2Count);
                Screen.PrintLine(temp);
            }
            LcdScreen.Delay(3000);
        }
    }
}