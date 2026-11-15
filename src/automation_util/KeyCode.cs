namespace game_automation_in_csharp.util
{
    // From http://www.kbdedit.com/manual/low_level_vk_list.html
    public class KeyCode
    {
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        public const uint VK_LBUTTON = 0x01;
        public const uint VK_RBUTTON = 0x02;
        public const uint VK_CANCEL = 0x03;
        public const uint VK_MBUTTON = 0x04;
        public const uint VK_XBUTTON1 = 0x05;
        public const uint VK_XBUTTON2 = 0x06;
        public const uint VK_BACK = 0x08;
        public const uint VK_TAB = 0x09;
        public const uint VK_CLEAR = 0x0C;
        public const uint VK_RETURN = 0x0D;
        public const uint VK_SHIFT = 0x10;
        public const uint VK_CONTROL = 0x11;
        public const uint VK_MENU = 0x12;
        public const uint VK_PAUSE = 0x13;
        public const uint VK_CAPITAL = 0x14;
        public const uint VK_KANA = 0x15;
        public const uint VK_HANGUL = 0x15;
        public const uint VK_JUNJA = 0x17;
        public const uint VK_FINAL = 0x18;
        public const uint VK_HANJA = 0x19;
        public const uint VK_KANJI = 0x19;
        public const uint VK_ESCAPE = 0x1B;
        public const uint VK_CONVERT = 0x1C;
        public const uint VK_NONCONVERT = 0x1D;
        public const uint VK_ACCEPT = 0x1E;
        public const uint VK_MODECHANGE = 0x1F;
        public const uint VK_SPACE = 0x20;
        public const uint VK_PRIOR = 0x21;
        public const uint VK_NEXT = 0x22;
        public const uint VK_END = 0x23;
        public const uint VK_HOME = 0x24;
        public const uint VK_LEFT = 0x25;
        public const uint VK_UP = 0x26;
        public const uint VK_RIGHT = 0x27;
        public const uint VK_DOWN = 0x28;
        public const uint VK_SELECT = 0x29;
        public const uint VK_PRINT = 0x2A;
        public const uint VK_EXECUTE = 0x2B;
        public const uint VK_SNAPSHOT = 0x2C;
        public const uint VK_INSERT = 0x2D;
        public const uint VK_DELETE = 0x2E;
        public const uint VK_HELP = 0x2F;

        // 0-9 and A-Z
        public const uint VK_0 = 0x30;
        public const uint VK_1 = 0x31;
        public const uint VK_2 = 0x32;
        public const uint VK_3 = 0x33;
        public const uint VK_4 = 0x34;
        public const uint VK_5 = 0x35;
        public const uint VK_6 = 0x36;
        public const uint VK_7 = 0x37;
        public const uint VK_8 = 0x38;
        public const uint VK_9 = 0x39;

        public const uint VK_A = 0x41;
        public const uint VK_B = 0x42;
        public const uint VK_C = 0x43;
        public const uint VK_D = 0x44;
        public const uint VK_E = 0x45;
        public const uint VK_F = 0x46;
        public const uint VK_G = 0x47;
        public const uint VK_H = 0x48;
        public const uint VK_I = 0x49;
        public const uint VK_J = 0x4A;
        public const uint VK_K = 0x4B;
        public const uint VK_L = 0x4C;
        public const uint VK_M = 0x4D;
        public const uint VK_N = 0x4E;
        public const uint VK_O = 0x4F;
        public const uint VK_P = 0x50;
        public const uint VK_Q = 0x51;
        public const uint VK_R = 0x52;
        public const uint VK_S = 0x53;
        public const uint VK_T = 0x54;
        public const uint VK_U = 0x55;
        public const uint VK_V = 0x56;
        public const uint VK_W = 0x57;
        public const uint VK_X = 0x58;
        public const uint VK_Y = 0x59;
        public const uint VK_Z = 0x5A;

        public const uint VK_LWIN = 0x5B;
        public const uint VK_RWIN = 0x5C;
        public const uint VK_APPS = 0x5D;
        public const uint VK_SLEEP = 0x5F;

        public const uint VK_NUMPAD0 = 0x60;
        public const uint VK_NUMPAD1 = 0x61;
        public const uint VK_NUMPAD2 = 0x62;
        public const uint VK_NUMPAD3 = 0x63;
        public const uint VK_NUMPAD4 = 0x64;
        public const uint VK_NUMPAD5 = 0x65;
        public const uint VK_NUMPAD6 = 0x66;
        public const uint VK_NUMPAD7 = 0x67;
        public const uint VK_NUMPAD8 = 0x68;
        public const uint VK_NUMPAD9 = 0x69;
        public const uint VK_MULTIPLY = 0x6A;
        public const uint VK_ADD = 0x6B;
        public const uint VK_SEPARATOR = 0x6C;
        public const uint VK_SUBTRACT = 0x6D;
        public const uint VK_DECIMAL = 0x6E;
        public const uint VK_DIVIDE = 0x6F;

        public const uint VK_F1 = 0x70;
        public const uint VK_F2 = 0x71;
        public const uint VK_F3 = 0x72;
        public const uint VK_F4 = 0x73;
        public const uint VK_F5 = 0x74;
        public const uint VK_F6 = 0x75;
        public const uint VK_F7 = 0x76;
        public const uint VK_F8 = 0x77;
        public const uint VK_F9 = 0x78;
        public const uint VK_F10 = 0x79;
        public const uint VK_F11 = 0x7A;
        public const uint VK_F12 = 0x7B;
        public const uint VK_F13 = 0x7C;
        public const uint VK_F14 = 0x7D;
        public const uint VK_F15 = 0x7E;
        public const uint VK_F16 = 0x7F;
        public const uint VK_F17 = 0x80;
        public const uint VK_F18 = 0x81;
        public const uint VK_F19 = 0x82;
        public const uint VK_F20 = 0x83;
        public const uint VK_F21 = 0x84;
        public const uint VK_F22 = 0x85;
        public const uint VK_F23 = 0x86;
        public const uint VK_F24 = 0x87;

        public const uint VK_NUMLOCK = 0x90;
        public const uint VK_SCROLL = 0x91;

        public const uint VK_LSHIFT = 0xA0;
        public const uint VK_RSHIFT = 0xA1;
        public const uint VK_LCONTROL = 0xA2;
        public const uint VK_RCONTROL = 0xA3;
        public const uint VK_LMENU = 0xA4;
        public const uint VK_RMENU = 0xA5;
        public const uint VK_BROWSER_BACK = 0xA6;
        public const uint VK_BROWSER_FORWARD = 0xA7;
        public const uint VK_BROWSER_REFRESH = 0xA8;
        public const uint VK_BROWSER_STOP = 0xA9;
        public const uint VK_BROWSER_SEARCH = 0xAA;
        public const uint VK_BROWSER_FAVORITES = 0xAB;
        public const uint VK_BROWSER_HOME = 0xAC;

        public const uint VK_VOLUME_MUTE = 0xAD;
        public const uint VK_VOLUME_DOWN = 0xAE;
        public const uint VK_VOLUME_UP = 0xAF;
        public const uint VK_MEDIA_NEXT_TRACK = 0xB0;
        public const uint VK_MEDIA_PREV_TRACK = 0xB1;
        public const uint VK_MEDIA_STOP = 0xB2;
        public const uint VK_MEDIA_PLAY_PAUSE = 0xB3;
        public const uint VK_LAUNCH_MAIL = 0xB4;
        public const uint VK_LAUNCH_MEDIA_SELECT = 0xB5;
        public const uint VK_LAUNCH_APP1 = 0xB6;
        public const uint VK_LAUNCH_APP2 = 0xB7;

        public const uint VK_OEM_1 = 0xBA;
    }
}
