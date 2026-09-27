using System.Text;

namespace ClipDesk.Plugin.QrCode;

public enum QrEccLevel
{
    Low = 0,      // ~7% recovery
    Medium = 1,   // ~15% recovery
    Quartile = 2, // ~25% recovery
    High = 3      // ~30% recovery
}

public sealed class QrCodeData
{
    private readonly bool[,] _modules;

    public int Size { get; }

    public QrCodeData(int size, bool[,] modules)
    {
        Size = size;
        _modules = modules;
    }

    public bool GetModule(int x, int y)
    {
        if (x < 0 || x >= Size || y < 0 || y >= Size)
            return false;
        return _modules[y, x];
    }
}

/// <summary>
/// Motor autocontido em C# puro para geração de códigos QR padrão ISO/IEC 18004.
/// Suporta UTF-8, versões 1 a 40 e níveis de correção de erro L, M, Q, H.
/// </summary>
public static class QrCodeEncoder
{
    public static QrCodeData EncodeText(string text, QrEccLevel ecc)
    {
        byte[] dataBytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        return EncodeBytes(dataBytes, ecc);
    }

    public static QrCodeData EncodeBytes(byte[] data, QrEccLevel ecc)
    {
        int version = FindSmallestVersion(data.Length, ecc);
        return GenerateQrCode(data, version, ecc);
    }

    private static int FindSmallestVersion(int byteCount, QrEccLevel ecc)
    {
        for (int v = 1; v <= 40; v++)
        {
            int capacity = GetDataCapacity(v, ecc);
            int headerBits = 4 + (v <= 9 ? 8 : 16);
            int totalBits = headerBits + (byteCount * 8);
            if (totalBits <= capacity * 8)
                return v;
        }

        throw new ArgumentException("O texto informado excede a capacidade máxima suportada pelo QR Code.");
    }

    private static QrCodeData GenerateQrCode(byte[] data, int version, QrEccLevel ecc)
    {
        int size = 17 + (version * 4);
        var modules = new bool[size, size];
        var isFunction = new bool[size, size];

        // 1. Padrões de alinhamento, localizadores e temporização
        DrawFunctionPatterns(modules, isFunction, version, size);

        // 2. Codificação dos dados e blocos de correção de erro
        byte[] fullCodewords = BuildCodewords(data, version, ecc);

        // 3. Escolher a melhor máscara (menor penalidade)
        int bestMask = 0;
        int minPenalty = int.MaxValue;
        bool[,] bestModules = new bool[size, size];

        for (int mask = 0; mask < 8; mask++)
        {
            var testModules = (bool[,])modules.Clone();
            DrawDataAndMask(testModules, isFunction, fullCodewords, size, mask);
            DrawFormatAndVersionInfo(testModules, version, ecc, mask, size);

            int penalty = CalculatePenalty(testModules, size);
            if (penalty < minPenalty)
            {
                minPenalty = penalty;
                bestMask = mask;
                bestModules = testModules;
            }
        }

        return new QrCodeData(size, bestModules);
    }

    private static void DrawFunctionPatterns(bool[,] modules, bool[,] isFunc, int version, int size)
    {
        // Localizadores (Finder patterns 7x7) nos cantos superior-esquerdo, superior-direito e inferior-esquerdo
        DrawFinder(modules, isFunc, 0, 0);
        DrawFinder(modules, isFunc, size - 7, 0);
        DrawFinder(modules, isFunc, 0, size - 7);

        // Separadores de 1 módulo ao redor dos localizadores
        DrawSeparators(modules, isFunc, size);

        // Padrões de temporização (Timing patterns) na linha 6 e coluna 6
        for (int i = 8; i < size - 8; i++)
        {
            bool val = (i % 2 == 0);
            SetFunctionModule(modules, isFunc, i, 6, val);
            SetFunctionModule(modules, isFunc, 6, i, val);
        }

        // Padrões de alinhamento (para versão >= 2)
        if (version >= 2)
        {
            int[] alignPositions = GetAlignmentPatternPositions(version);
            for (int r = 0; r < alignPositions.Length; r++)
            {
                for (int c = 0; c < alignPositions.Length; c++)
                {
                    int x = alignPositions[c];
                    int y = alignPositions[r];
                    if (isFunc[y, x]) continue; // Não sobrepor localizadores

                    for (int dy = -2; dy <= 2; dy++)
                    {
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            bool val = (Math.Abs(dx) == 2 || Math.Abs(dy) == 2 || (dx == 0 && dy == 0));
                            SetFunctionModule(modules, isFunc, x + dx, y + dy, val);
                        }
                    }
                }
            }
        }

        // Módulo escuro permanente (Dark module) em (8, 4*version + 9)
        SetFunctionModule(modules, isFunc, 8, (4 * version) + 9, true);

        // Reservar áreas de informação de formato (ao redor dos localizadores)
        for (int i = 0; i <= 8; i++)
        {
            if (!isFunc[8, i]) isFunc[8, i] = true;
            if (!isFunc[i, 8]) isFunc[i, 8] = true;
        }
        for (int i = 0; i <= 7; i++)
        {
            if (!isFunc[8, size - 1 - i]) isFunc[8, size - 1 - i] = true;
            if (!isFunc[size - 1 - i, 8]) isFunc[size - 1 - i, 8] = true;
        }

        // Reservar áreas de informação de versão (versão >= 7)
        if (version >= 7)
        {
            for (int i = 0; i < 6; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    isFunc[i, size - 11 + j] = true;
                    isFunc[size - 11 + j, i] = true;
                }
            }
        }
    }

    private static void DrawFinder(bool[,] modules, bool[,] isFunc, int startX, int startY)
    {
        for (int dy = 0; dy < 7; dy++)
        {
            for (int dx = 0; dx < 7; dx++)
            {
                bool val = (dx == 0 || dx == 6 || dy == 0 || dy == 6 || (dx >= 2 && dx <= 4 && dy >= 2 && dy <= 4));
                SetFunctionModule(modules, isFunc, startX + dx, startY + dy, val);
            }
        }
    }

    private static void DrawSeparators(bool[,] modules, bool[,] isFunc, int size)
    {
        for (int i = 0; i < 8; i++)
        {
            SetFunctionModule(modules, isFunc, 7, i, false);
            SetFunctionModule(modules, isFunc, i, 7, false);
            SetFunctionModule(modules, isFunc, size - 8, i, false);
            SetFunctionModule(modules, isFunc, size - 1 - i, 7, false);
            SetFunctionModule(modules, isFunc, 7, size - 1 - i, false);
            SetFunctionModule(modules, isFunc, i, size - 8, false);
        }
    }

    private static void SetFunctionModule(bool[,] modules, bool[,] isFunc, int x, int y, bool value)
    {
        modules[y, x] = value;
        isFunc[y, x] = true;
    }

    private static byte[] BuildCodewords(byte[] data, int version, QrEccLevel ecc)
    {
        int totalDataBytes = GetDataCapacity(version, ecc);
        var bitList = new List<bool>();

        // Modo Byte (0100)
        AddBits(bitList, 0b0100, 4);

        // Contador de caracteres
        int countBits = version <= 9 ? 8 : 16;
        AddBits(bitList, data.Length, countBits);

        // Dados UTF-8
        foreach (byte b in data)
            AddBits(bitList, b, 8);

        // Terminador (até 4 zeros)
        int term = Math.Min(4, (totalDataBytes * 8) - bitList.Count);
        AddBits(bitList, 0, term);

        // Preenchimento de bits até múltiplo de 8
        while (bitList.Count % 8 != 0)
            bitList.Add(false);

        // Preenchimento com bytes alternados 0xEC (236) e 0x11 (17)
        byte[] padBytes = [0xEC, 0x11];
        int padIndex = 0;
        while (bitList.Count < totalDataBytes * 8)
        {
            AddBits(bitList, padBytes[padIndex % 2], 8);
            padIndex++;
        }

        // Converter bits para bytes de dados
        byte[] dataCodewords = new byte[totalDataBytes];
        for (int i = 0; i < bitList.Count; i++)
        {
            if (bitList[i])
                dataCodewords[i / 8] |= (byte)(1 << (7 - (i % 8)));
        }

        // Dividir em blocos e calcular códigos de correção de erro Reed-Solomon
        int numBlocks = GetNumBlocks(version, ecc);
        int ecCodewordsPerBlock = GetEcCodewordsPerBlock(version, ecc);
        int totalEccCodewords = numBlocks * ecCodewordsPerBlock;

        int shortBlockLen = totalDataBytes / numBlocks;
        int numShortBlocks = numBlocks - (totalDataBytes % numBlocks);
        int longBlockLen = shortBlockLen + 1;

        byte[][] dataBlocks = new byte[numBlocks][];
        byte[][] ecBlocks = new byte[numBlocks][];

        int offset = 0;
        for (int b = 0; b < numBlocks; b++)
        {
            int len = (b < numShortBlocks) ? shortBlockLen : longBlockLen;
            dataBlocks[b] = new byte[len];
            Array.Copy(dataCodewords, offset, dataBlocks[b], 0, len);
            offset += len;

            ecBlocks[b] = CalculateReedSolomon(dataBlocks[b], ecCodewordsPerBlock);
        }

        // Intercalar blocos de dados e blocos de ECC
        byte[] result = new byte[totalDataBytes + totalEccCodewords];
        int writePos = 0;

        for (int i = 0; i < longBlockLen; i++)
        {
            for (int b = 0; b < numBlocks; b++)
            {
                if (i < dataBlocks[b].Length)
                    result[writePos++] = dataBlocks[b][i];
            }
        }

        for (int i = 0; i < ecCodewordsPerBlock; i++)
        {
            for (int b = 0; b < numBlocks; b++)
            {
                result[writePos++] = ecBlocks[b][i];
            }
        }

        return result;
    }

    private static void AddBits(List<bool> bits, int value, int count)
    {
        for (int i = count - 1; i >= 0; i--)
            bits.Add(((value >> i) & 1) != 0);
    }

    private static void DrawDataAndMask(bool[,] modules, bool[,] isFunc, byte[] codewords, int size, int mask)
    {
        int bitIndex = 0;
        int totalBits = codewords.Length * 8;

        for (int right = size - 1; right > 0; right -= 2)
        {
            if (right == 6) right--; // Pular coluna de temporização

            for (int vert = 0; vert < size; vert++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upwards = ((right + 1) & 2) == 0;
                    int y = upwards ? size - 1 - vert : vert;

                    if (isFunc[y, x]) continue;

                    bool bit = false;
                    if (bitIndex < totalBits)
                    {
                        int bytePos = bitIndex / 8;
                        int bitPos = 7 - (bitIndex % 8);
                        bit = ((codewords[bytePos] >> bitPos) & 1) != 0;
                        bitIndex++;
                    }

                    bool maskBit = IsMasked(mask, x, y);
                    modules[y, x] = bit ^ maskBit;
                }
            }
        }
    }

    private static bool IsMasked(int mask, int x, int y) => mask switch
    {
        0 => (x + y) % 2 == 0,
        1 => y % 2 == 0,
        2 => x % 3 == 0,
        3 => (x + y) % 3 == 0,
        4 => ((y / 2) + (x / 3)) % 2 == 0,
        5 => ((x * y) % 2) + ((x * y) % 3) == 0,
        6 => (((x * y) % 2) + ((x * y) % 3)) % 2 == 0,
        7 => (((x + y) % 2) + ((x * y) % 3)) % 2 == 0,
        _ => false
    };

    private static void DrawFormatAndVersionInfo(bool[,] modules, int version, QrEccLevel ecc, int mask, int size)
    {
        // 15 bits de formato: 2 bits ECC, 3 bits máscara, 10 bits BCH de erro
        int eccBits = ecc switch
        {
            QrEccLevel.Low => 1,
            QrEccLevel.Medium => 0,
            QrEccLevel.Quartile => 3,
            QrEccLevel.High => 2,
            _ => 0
        };
        int formatData = (eccBits << 3) | mask;
        int rem = formatData;
        for (int i = 0; i < 10; i++)
            rem = (rem << 1) ^ ((rem >> 9) * 0x537);

        int formatBits = ((formatData << 10) | rem) ^ 0x5412; // Máscara XOR fixa

        for (int i = 0; i <= 5; i++)
            modules[8, i] = GetBit(formatBits, i);
        modules[8, 7] = GetBit(formatBits, 6);
        modules[8, 8] = GetBit(formatBits, 7);
        modules[7, 8] = GetBit(formatBits, 8);
        for (int i = 9; i <= 14; i++)
            modules[14 - i, 8] = GetBit(formatBits, i);

        for (int i = 0; i <= 7; i++)
            modules[8, size - 1 - i] = GetBit(formatBits, i);
        for (int i = 8; i <= 14; i++)
            modules[size - 15 + i, 8] = GetBit(formatBits, i);

        // Versão >= 7
        if (version >= 7)
        {
            int vData = version;
            int vRem = vData;
            for (int i = 0; i < 12; i++)
                vRem = (vRem << 1) ^ ((vRem >> 11) * 0x1F25);
            int versionBits = (vData << 12) | vRem;

            for (int i = 0; i < 18; i++)
            {
                bool bit = GetBit(versionBits, i);
                int a = size - 11 + (i % 3);
                int b = i / 3;
                modules[a, b] = bit;
                modules[b, a] = bit;
            }
        }
    }

    private static bool GetBit(int value, int index) => ((value >> index) & 1) != 0;

    private static int CalculatePenalty(bool[,] modules, int size)
    {
        int penalty = 0;

        // Regra 1: Linhas e colunas com 5 ou mais módulos consecutivos de mesma cor
        for (int y = 0; y < size; y++)
        {
            int run = 0;
            bool? last = null;
            for (int x = 0; x < size; x++)
            {
                if (modules[y, x] == last)
                {
                    run++;
                    if (run == 5) penalty += 3;
                    else if (run > 5) penalty++;
                }
                else
                {
                    last = modules[y, x];
                    run = 1;
                }
            }
        }

        for (int x = 0; x < size; x++)
        {
            int run = 0;
            bool? last = null;
            for (int y = 0; y < size; y++)
            {
                if (modules[y, x] == last)
                {
                    run++;
                    if (run == 5) penalty += 3;
                    else if (run > 5) penalty++;
                }
                else
                {
                    last = modules[y, x];
                    run = 1;
                }
            }
        }

        // Regra 2: Blocos 2x2 da mesma cor
        for (int y = 0; y < size - 1; y++)
        {
            for (int x = 0; x < size - 1; x++)
            {
                bool c = modules[y, x];
                if (c == modules[y + 1, x] && c == modules[y, x + 1] && c == modules[y + 1, x + 1])
                    penalty += 3;
            }
        }

        // Regra 3: Padrão 1:1:3:1:1
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size - 6; x++)
            {
                if (modules[y, x] && !modules[y, x + 1] && modules[y, x + 2] && modules[y, x + 3] &&
                    modules[y, x + 4] && !modules[y, x + 5] && modules[y, x + 6])
                {
                    if (x >= 4 && !modules[y, x - 1] && !modules[y, x - 2] && !modules[y, x - 3] && !modules[y, x - 4])
                        penalty += 40;
                    if (x + 10 < size && !modules[y, x + 7] && !modules[y, x + 8] && !modules[y, x + 9] && !modules[y, x + 10])
                        penalty += 40;
                }
            }
        }

        // Regra 4: Razão de módulos escuros
        int darkCount = 0;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                if (modules[y, x]) darkCount++;

        int totalModules = size * size;
        int percent = (darkCount * 100) / totalModules;
        int prevMultiple = (percent / 5) * 5;
        int nextMultiple = prevMultiple + 5;
        penalty += Math.Min(Math.Abs(percent - 50) / 5, Math.Abs(nextMultiple - 50) / 5) * 10;

        return penalty;
    }

    #region Galois Field & Reed-Solomon

    private static readonly byte[] ExpTable = new byte[256];
    private static readonly byte[] LogTable = new byte[256];

    static QrCodeEncoder()
    {
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            ExpTable[i] = (byte)x;
            LogTable[x] = (byte)i;
            x <<= 1;
            if ((x & 0x100) != 0)
                x ^= 0x11D; // Polinômio primitivo x^8 + x^4 + x^3 + x^2 + 1
        }
        for (int i = 255; i < 512; i++)
        {
            // ExpTable repetida para evitar módulo
        }
    }

    private static byte GfMultiply(byte a, byte b)
    {
        if (a == 0 || b == 0) return 0;
        int logSum = LogTable[a] + LogTable[b];
        return ExpTable[logSum % 255];
    }

    private static byte[] CalculateReedSolomon(byte[] data, int ecCount)
    {
        // Polinômio gerador para ecCount
        byte[] generator = [1];
        for (int i = 0; i < ecCount; i++)
        {
            byte root = ExpTable[i];
            byte[] next = new byte[generator.Length + 1];
            for (int j = 0; j < generator.Length; j++)
            {
                next[j] ^= generator[j];
                next[j + 1] ^= GfMultiply(generator[j], root);
            }
            generator = next;
        }

        byte[] result = new byte[ecCount];
        foreach (byte d in data)
        {
            byte factor = (byte)(d ^ result[0]);
            Array.Copy(result, 1, result, 0, ecCount - 1);
            result[ecCount - 1] = 0;

            for (int j = 0; j < ecCount; j++)
                result[j] ^= GfMultiply(generator[j + 1], factor);
        }

        return result;
    }

    #endregion

    #region Tabelas de Versão, Capacidade e ECC

    private static readonly int[,] EcCodewordsTable = {
        // L, M, Q, H
        { 7, 10, 13, 17 }, { 10, 16, 22, 28 }, { 15, 26, 36, 44 }, { 20, 36, 52, 64 },
        { 26, 48, 72, 88 }, { 36, 64, 96, 112 }, { 40, 72, 108, 130 }, { 48, 88, 132, 156 },
        { 60, 110, 160, 192 }, { 72, 130, 192, 224 }, { 80, 150, 224, 264 }, { 96, 176, 260, 308 },
        { 104, 198, 288, 352 }, { 120, 216, 320, 384 }, { 132, 240, 360, 432 }, { 144, 280, 408, 480 },
        { 168, 308, 448, 532 }, { 180, 338, 504, 588 }, { 196, 364, 546, 650 }, { 224, 416, 600, 700 },
        { 224, 442, 644, 750 }, { 252, 476, 690, 816 }, { 270, 504, 750, 900 }, { 300, 560, 810, 960 },
        { 312, 588, 870, 1050 }, { 336, 644, 952, 1110 }, { 360, 700, 1020, 1200 }, { 390, 728, 1050, 1260 },
        { 420, 784, 1140, 1350 }, { 450, 812, 1200, 1440 }, { 480, 868, 1290, 1530 }, { 510, 924, 1350, 1620 },
        { 540, 980, 1440, 1710 }, { 570, 1036, 1530, 1800 }, { 600, 1064, 1590, 1890 }, { 630, 1120, 1680, 1980 },
        { 660, 1204, 1770, 2100 }, { 720, 1260, 1860, 2220 }, { 750, 1316, 1950, 2310 }, { 780, 1372, 2040, 2430 }
    };

    private static readonly int[,] NumBlocksTable = {
        // L, M, Q, H
        { 1, 1, 1, 1 }, { 1, 1, 1, 1 }, { 1, 1, 2, 2 }, { 1, 2, 2, 4 },
        { 1, 2, 4, 4 }, { 2, 4, 4, 4 }, { 2, 4, 6, 5 }, { 2, 4, 6, 6 },
        { 2, 5, 8, 8 }, { 4, 5, 8, 8 }, { 4, 5, 8, 11 }, { 4, 8, 10, 11 },
        { 4, 9, 12, 16 }, { 4, 9, 16, 16 }, { 6, 10, 12, 18 }, { 6, 10, 17, 16 },
        { 6, 11, 16, 19 }, { 6, 13, 18, 21 }, { 7, 14, 21, 25 }, { 8, 16, 20, 25 },
        { 8, 17, 23, 25 }, { 9, 17, 23, 34 }, { 9, 18, 25, 30 }, { 10, 20, 27, 32 },
        { 12, 21, 29, 35 }, { 12, 23, 34, 37 }, { 12, 25, 34, 40 }, { 13, 26, 35, 42 },
        { 14, 28, 38, 45 }, { 15, 29, 40, 48 }, { 16, 31, 43, 51 }, { 17, 33, 45, 54 },
        { 18, 35, 48, 57 }, { 19, 37, 51, 60 }, { 19, 38, 53, 63 }, { 20, 40, 56, 66 },
        { 21, 43, 59, 70 }, { 22, 45, 62, 74 }, { 24, 47, 65, 77 }, { 25, 49, 68, 81 }
    };

    private static readonly int[] TotalCodewordsTable = {
        26, 44, 70, 100, 134, 172, 196, 242, 292, 346, 404, 466, 532, 581, 655, 733,
        815, 901, 991, 1085, 1156, 1258, 1364, 1474, 1588, 1706, 1828, 1921, 2051,
        2185, 2323, 2465, 2611, 2761, 2876, 3034, 3196, 3362, 3532, 3706
    };

    private static int GetDataCapacity(int version, QrEccLevel ecc)
    {
        int total = TotalCodewordsTable[version - 1];
        int eccCount = EcCodewordsTable[version - 1, (int)ecc];
        return total - eccCount;
    }

    private static int GetNumBlocks(int version, QrEccLevel ecc) => NumBlocksTable[version - 1, (int)ecc];

    private static int GetEcCodewordsPerBlock(int version, QrEccLevel ecc)
    {
        int totalEcc = EcCodewordsTable[version - 1, (int)ecc];
        int blocks = GetNumBlocks(version, ecc);
        return totalEcc / blocks;
    }

    private static int[] GetAlignmentPatternPositions(int version)
    {
        if (version == 1) return [];
        int num = (version / 7) + 2;
        int step = (version == 32) ? 26 : ((version * 4 + num * 2 + 1) / (num * 2 - 2)) * 2;
        int[] result = new int[num];
        result[0] = 6;
        for (int i = num - 1, pos = 4 * version + 10; i >= 1; i--, pos -= step)
            result[i] = pos;
        return result;
    }

    #endregion
}
