using System.ComponentModel;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace StockfishHarnessLibNamespace
{
    public class StockfishHarnessLib
    {
        #region Member Variables
        const String sfEngineExe = "D:\\Chess\\Stockfish\\stockfish-windows-x86-64-avx2.exe";
        const String syzygyDir = "D:\\Chess\\Tablebases\\syzygy\\3-4-5piecesSyzygy\\3-4-5";

        const String bestMoveToken = "bestmove ";
        const String infoDepthToken = "info depth ";
        const String mateToken = "score mate ";
        public const String movesToken = " pv ";
        const String scoreToken = "score cp ";
        const String stockfishToken = "Stockfish";
        const String stockfishVersionToken = "id name Stockfish";
        const String uciokToken = "uciok";
        public bool IsChess960 { get; private set; } = false;
        public string StockfishVersion { get; private set; } = "";
        private Process sfProcess = null!;
        private Dictionary<String, (int CpScore, int InfoDepth)> sfDict = null!;
        #endregion Member Variables
        #region Constructors
        private StockfishHarnessLib()
        {
        }
        public StockfishHarnessLib(bool _IsChess960 = false)
        {
            IsChess960 = _IsChess960;
            StartStockfish();
        }
        #endregion Constructors
        #region Methods
        private void StartStockfish()
        {
            ProcessStartInfo sfStartInfo = new()
            {
                UseShellExecute = false, //required to redirect standart input/output

                // redirects on your choice
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,

                FileName = sfEngineExe,
                Arguments = ""
            };

            sfProcess = new()
            {
                StartInfo = sfStartInfo
            };
            sfProcess.Start();

            sfProcess.StandardInput.WriteLine("isready");
            sfProcess.StandardInput.WriteLine("uci");
            sfProcess.StandardInput.WriteLine("setoption name threads value 8");
            sfProcess.StandardInput.WriteLine("setoption name Hash value 32768");
            sfProcess.StandardInput.WriteLine("setoption name ponder value false");
            sfProcess.StandardInput.WriteLine("setoption name MultiPv value 7");
            sfProcess.StandardInput.WriteLine("setoption name Slow Mover value 100");
            sfProcess.StandardInput.WriteLine("setoption name SyzygyPath value " + syzygyDir);
            if (IsChess960)
                sfProcess.StandardInput.WriteLine("setoption name UCI_Chess960 value true");
            StringBuilder sb = new();
            while (!sfProcess.StandardOutput.EndOfStream)
            {
                String? sfLine = sfProcess.StandardOutput.ReadLine();
                if (sfLine == null)
                    continue;
                sb.AppendLine(sfLine);
                Console.WriteLine(sfLine);
                if (sfLine.Contains(stockfishVersionToken, StringComparison.CurrentCulture))
                {
                    int index = sfLine.IndexOf(stockfishToken);
                    if (index != -1)
                        StockfishVersion = sfLine[index..];
                }
                else
                {
                    if (sfLine.Contains(uciokToken, StringComparison.CurrentCulture))
                        break;
                }
            }
            sfDict = [];
        }
        public string AnalyzeFen(String _Fen, int _AnalysisDepth, int _MinAnalysisDepth, int _CpLossThreshold)
        {
            sfProcess.StandardInput.WriteLine("ucinewgame");
            sfProcess.StandardInput.WriteLine("position fen " + _Fen);
            sfProcess.StandardInput.WriteLine("go depth " + _AnalysisDepth.ToString());
            StringBuilder sb = new();
            sb.AppendLine(StockfishVersion);
            sb.AppendLine("position fen " + _Fen);
            String sbString = "";
            while (!sfProcess.StandardOutput.EndOfStream)
            {
                String? sfLine = sfProcess.StandardOutput.ReadLine();
                if (sfLine == null)
                    continue;
                sb.AppendLine(sfLine);
                //Console.WriteLine(sfLine);
                if (sfLine.Contains(bestMoveToken, StringComparison.CurrentCulture))
                {
                    sbString = HandleMate(sb);
                    break;
                }
            }
            sfDict.Clear();
            int bestMoveIdx = sbString.IndexOf(bestMoveToken);
            int infoDepthIdx = sbString.IndexOf(infoDepthToken);
            Int32 st, en;
            while (infoDepthIdx >= 0)
            {
                st = infoDepthIdx + infoDepthToken.Length;
                en = sbString.IndexOf(' ', st);
                String infoDepthTxt = sbString[st..en];
                if (!int.TryParse(infoDepthTxt, out int infoDepth))
                    Debug.Assert(false, "Info depth not numeric >" + infoDepthTxt + " for " + _Fen);
                st = sbString.IndexOf(scoreToken, en + 1);
                if (st < 0)
                {
                    infoDepthIdx = -1;
                    continue;
                }
                st += scoreToken.Length;
                en = sbString.IndexOf(' ', st);
                String cpScoreTxt = sbString[st..en];
                if (!int.TryParse(cpScoreTxt, out int cpScore))
                    Debug.Assert(false, "CP score not numeric >" + cpScoreTxt + " for " + _Fen);
                st = sbString.IndexOf(movesToken, en + 1) + movesToken.Length;
                en = sbString.IndexOf(Environment.NewLine, st);
                string movesTxt = sbString[st..en];
                infoDepthIdx = sbString.IndexOf(infoDepthToken, en);
                if (infoDepth < _MinAnalysisDepth)
                    continue;
                String[] movesArray = movesTxt.Split(' ');
                (int CpScore, int InfoDepth) variationEvalTuple = new(cpScore, infoDepth);
                String variationTxt = movesArray[0];
                if (!sfDict.TryAdd(variationTxt, variationEvalTuple))
                    sfDict[variationTxt] = variationEvalTuple;
            }
            st = bestMoveIdx + bestMoveToken.Length;
            en = sbString.IndexOf(' ', st);
            if (en < 0)
                en = sbString.IndexOf(Environment.NewLine, st);
            String bestMoveTxt = sbString[st..en];
            int bestMoveCp = sfDict[bestMoveTxt].CpScore;
            List<(int CpScore, int InfoDepth, string MoveTxt)> returnList = [];
            foreach (KeyValuePair<String, (int CpScore, int InfoDepth)> kvp in sfDict)
            {
                bool isInsert = bestMoveCp - kvp.Value.CpScore <= _CpLossThreshold;
                if (isInsert)
                    returnList.Add((kvp.Value.CpScore, kvp.Value.InfoDepth, kvp.Key));
            }

            // Sort by CpScore ascending, then by InfoDepth descending
            returnList.Sort((x, y) =>
            {
                int result = y.CpScore.CompareTo(x.CpScore);
                if (result == 0)
                {
                    result = y.InfoDepth.CompareTo(x.InfoDepth);
                }
                return result;
            });
            return CreateJsonResult(StockfishVersion, _Fen, _AnalysisDepth, _MinAnalysisDepth, _CpLossThreshold, returnList);

        }
        #endregion Methods
        #region Static Methods
        static String HandleMate(StringBuilder _Sb)
        {
            while (true)
            {
                String workStr = _Sb.ToString();
                Int32 st = workStr.IndexOf(mateToken);
                if (st < 0)
                    break;
                st += mateToken.Length;
                Int32 en = workStr.IndexOf(' ', st);
                String token = workStr[st..en];
                if (token[0] == '-')
                    _Sb.Replace(mateToken + token + ' ', "score cp -32767 ");
                else
                    _Sb.Replace(mateToken + token + ' ', "score cp 32767 ");
            }
            return _Sb.ToString();
        }
        public static IEnumerable<(int Id960, string Fen960)> GetChess960Fen()
        {
            char[] firstRank = new char[8];
            for (int id = 0; id < 960; id++)
            {
                Array.Clear(firstRank, 0, firstRank.Length);

                // a) Divide N by 4, yielding quotient N2 and remainder B1. Place a Bishop upon the bright square corresponding to B1 (0 = b, 1 = d, 2 = f, 3 = h).
                int N2 = Math.DivRem(id, 4, out int B1);
                firstRank[2 * B1 + 1] = 'b';

                // b) Divide N2 by 4 again, yielding quotient N3 and remainder B2. Place a second Bishop upon the dark square corresponding to B2 (0 = a, 1 = c, 2 = e, 3 = g).
                int N3 = Math.DivRem(N2, 4, out int B2);
                firstRank[2 * B2] = 'b';

                // c) Divide N3 by 6, yielding quotient N4 and remainder Q. Place the Queen according to Q, where 0 is the first free square starting from a, 1 is the second, etc.
                int N4 = Math.DivRem(N3, 6, out int Q);
                for (int sq = 0; sq < firstRank.Length; sq++)
                {
                    if (firstRank[sq] == 0)
                    {
                        if (Q == 0)
                        {
                            firstRank[sq] = 'q';
                            break;
                        }
                        else
                        {
                            Q--;
                        }
                    }
                }

                // d) N4 will be a single digit, 0...9. Ignoring Bishops and Queen, find the positions of two Knights within the remaining five spaces.
                int[] knightPositions = [0, 1, 2, 3, 4, 5, 6, 7];
                knightPositions = knightPositions.Where(pos => firstRank[pos] == 0).ToArray();
                (int K1, int K2)[] knightCombinations = [(0, 1), (0, 2), (0, 3), (0, 4), (1, 2), (1, 3), (1, 4), (2, 3), (2, 4), (3, 4)];
                //if (id == 96)
                //    Debug.Assert(true);
                firstRank[knightPositions[knightCombinations[N4].K1]] = 'n';
                firstRank[knightPositions[knightCombinations[N4].K2]] = 'n';

                // Place the Rooks and King in the remaining spaces
                int[] remainingPositions = [0, 1, 2, 3, 4, 5, 6, 7];
                remainingPositions = remainingPositions.Where(pos => firstRank[pos] == 0).ToArray();
                firstRank[remainingPositions[0]] = 'r';
                firstRank[remainingPositions[1]] = 'k';
                firstRank[remainingPositions[2]] = 'r';

                // Create the FEN string for this position
                string fen960 = new string(firstRank) + "/pppppppp/8/8/8/8/PPPPPPPP/" + new string(firstRank).ToUpper() + " w KQkq - 0 1";
                yield return (id, fen960);
            }
        }
        public static string CreateJsonResult(string stockfishVersion, string _Fen, int _AnalysisDepth, int _MinAnalysisDepth, int _CpLossThreshold, List<(int CpScore, int InfoDepth, string MoveTxt)> returnList)
        {
            var result = new
            {
                StockfishVersion = stockfishVersion,
                Fen = _Fen,
                AnalysisDepth = _AnalysisDepth,
                MinAnalysisDepth = _MinAnalysisDepth,
                CpLossThreshold = _CpLossThreshold,
                Variations = returnList.Select(v => new { v.CpScore, v.InfoDepth, v.MoveTxt }).ToList()
            };
            return JsonSerializer.Serialize(result);
        }
        public static AnalysisResult DeserializeJsonResult(string jsonString)
        {
            return JsonSerializer.Deserialize<AnalysisResult>(jsonString) ?? new AnalysisResult();
        }
        #endregion Static Methods
    }
    #region JSON Classes
    public class AnalysisResult
    {
        public string StockfishVersion { get; set; } = string.Empty;
        public string Fen { get; set; } = string.Empty;
        public int AnalysisDepth { get; set; }
        public int MinAnalysisDepth { get; set; }
        public int CpLossThreshold { get; set; }
        public List<Variation> Variations { get; set; } = [];
    }
    public class Variation
    {
        public int CpScore { get; set; }
        public int InfoDepth { get; set; }
        public string MoveTxt { get; set; } = string.Empty;
    }
#endregion JSON Classes
}
