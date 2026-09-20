using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace CapToPath
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 1. Flags check
            bool silent = args != null && args.Any(a => string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase) ||
                                                        string.Equals(a, "-s", StringComparison.OrdinalIgnoreCase));
            bool help = args != null && args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
                                                      string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase) ||
                                                      string.Equals(a, "/?", StringComparison.OrdinalIgnoreCase));

            if (help)
            {
                ShowUsageGuide();
                return;
            }

            // Filter out flags to get path arguments
            string[] pathArgs = args != null
                ? args.Where(a => !a.StartsWith("-", StringComparison.Ordinal) && !a.StartsWith("/?", StringComparison.Ordinal)).ToArray()
                : Array.Empty<string>();

            // 2. Resolve target file path with PicPick auto-detection and whitespace handling
            string resolvedPath = ResolveFilePath(pathArgs);

            // If no path could be resolved at all
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                if (pathArgs.Length == 0)
                {
                    // Manually opened without arguments and no recent screenshot found
                    ShowUsageGuide();
                    return;
                }

                string rawJoined = string.Join(" ", pathArgs);
                LogDebug($"Error: Empty resolved path for raw input: {rawJoined}");
                if (!silent)
                {
                    Application.Run(new ToastForm("오류: 스크린샷 파일을 찾을 수 없습니다.", rawJoined, isSuccess: false));
                }
                return;
            }

            // 3. Copy to Windows Clipboard using Native Win32 API + Retry logic
            bool copySuccess = ClipboardHelper.CopyText(resolvedPath, out string errorDetail);

            LogDebug($"Path: {resolvedPath} | Success: {copySuccess} | Error: {errorDetail}");

            // 4. Show toast notification or exit
            if (!silent)
            {
                if (copySuccess)
                {
                    Application.Run(new ToastForm("스크린샷 경로가 복사되었습니다.", resolvedPath, isSuccess: true));
                }
                else
                {
                    string failMsg = string.IsNullOrWhiteSpace(errorDetail)
                        ? resolvedPath
                        : $"{errorDetail} ({Path.GetFileName(resolvedPath)})";
                    Application.Run(new ToastForm("클립보드 복사에 실패했습니다.", failMsg, isSuccess: false));
                }
            }
        }

        private static void LogDebug(string message)
        {
            try
            {
                string tempDir = Path.GetTempPath();
                string logFile = Path.Combine(tempDir, "CapToPath_last.log");
                File.WriteAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n");
            }
            catch
            {
                // Ignore logging failures
            }
        }

        /// <summary>
        /// Robustly resolves file path:
        /// 1. If valid file path is passed, verifies and uses it.
        /// 2. If PicPick passed unreplaced "%1" placeholder or invalid path, auto-detects the latest screenshot from PicPick's AutoSaveFolder.
        /// </summary>
        private static string ResolveFilePath(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                string candidateSingle = CleanPathString(args[0]);
                string candidateJoined = CleanPathString(string.Join(" ", args));

                // Check if argument is a PicPick template token that was not replaced (e.g. "%1", "%f")
                bool isPlaceholder = IsTemplatePlaceholder(candidateSingle) || IsTemplatePlaceholder(candidateJoined);

                if (!isPlaceholder)
                {
                    // Check if file actually exists (wait briefly up to 600ms for PicPick disk write)
                    for (int attempt = 0; attempt < 12; attempt++)
                    {
                        if (File.Exists(candidateSingle))
                        {
                            return NormalizePath(candidateSingle);
                        }

                        if (args.Length > 1 && File.Exists(candidateJoined))
                        {
                            return NormalizePath(candidateJoined);
                        }

                        Thread.Sleep(50);
                    }

                    // If candidate exists as an absolute path format and has image extension
                    string candidate = args.Length > 1 ? candidateJoined : candidateSingle;
                    if (Path.IsPathRooted(candidate) && File.Exists(NormalizePath(candidate)))
                    {
                        return NormalizePath(candidate);
                    }
                }
            }

            // Fallback: PicPick passed unreplaced "%1", or file was not found by argument.
            // Automatically find the most recently created screenshot from PicPick's AutoSaveFolder!
            string? detectedScreenshot = PicPickHelper.DetectLatestScreenshot(timeoutMs: 1500);
            if (!string.IsNullOrWhiteSpace(detectedScreenshot))
            {
                return NormalizePath(detectedScreenshot);
            }

            // Ultimate fallback: best guess from arguments if present
            if (args != null && args.Length > 0)
            {
                string bestGuess = args.Length > 1 ? string.Join(" ", args) : args[0];
                if (!IsTemplatePlaceholder(bestGuess))
                {
                    return NormalizePath(CleanPathString(bestGuess));
                }
            }

            return string.Empty;
        }

        private static bool IsTemplatePlaceholder(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string clean = text.Trim().Trim('"', '\'');
            return clean.Equals("%1", StringComparison.OrdinalIgnoreCase) ||
                   clean.Equals("%f", StringComparison.OrdinalIgnoreCase) ||
                   clean.EndsWith("%1") ||
                   clean.EndsWith("%f");
        }

        private static string CleanPathString(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            string cleaned = text.Trim().Trim('"', '\'', ' ', '\t', '\r', '\n');

            // Handle trailing quotes or slashes that can occur with escaped arguments like \"path\"
            cleaned = cleaned.TrimEnd('"', '\'');

            // If path ends with a slash/backslash but has a file extension (e.g. .png, .jpg), strip the trailing separator
            if (cleaned.EndsWith('\\') || cleaned.EndsWith('/'))
            {
                string candidate = cleaned.TrimEnd('\\', '/');
                string ext = Path.GetExtension(candidate);
                if (!string.IsNullOrEmpty(ext) && ext.Length >= 2 && ext.Length <= 6)
                {
                    cleaned = candidate;
                }
            }

            return cleaned;
        }

        private static string NormalizePath(string rawPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawPath)) return string.Empty;
                string full = Path.GetFullPath(rawPath);

                // If it's a file path with an extension and not an existing directory, strip any accidental trailing slash
                if (!Directory.Exists(full))
                {
                    string trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string ext = Path.GetExtension(trimmed);
                    if (!string.IsNullOrEmpty(ext))
                    {
                        return trimmed;
                    }
                }

                return full;
            }
            catch
            {
                return CleanPathString(rawPath);
            }
        }

        private static void ShowUsageGuide()
        {
            string message =
                "CapToPath v1.1 - 픽픽 연동형 스크린샷 경로 복사 유틸리티\n\n" +
                "■ 기능:\n" +
                "  스크린샷 캡처 시 이미지 절대 경로를 윈도우 클립보드에 자동 복사합니다.\n" +
                "  (노션, 디스코드, 개발 툴 등에서 Ctrl + V로 즉시 붙여넣기 가능)\n\n" +
                "■ 스마트 자동 감지:\n" +
                "  픽픽 설정의 자동 저장 폴더를 분석하여 캡처 직후 생성된 최신 이미지 경로를 자동으로 찾아 복사합니다.\n\n" +
                "■ 픽픽(PicPick) 연동 설정 방법:\n" +
                "  1. 픽픽 [파일] -> [프로그램 설정] -> [외부 프로그램] 이동\n" +
                "  2. 프로그램 추가: CapToPath.exe 선택\n" +
                "  3. 매개변수 항목: 빈칸 또는 \"%1\" (어떤 설정이든 자동 감지 작동)\n" +
                "  4. [캡처] -> [캡처 후 결과 작업]을 '외부 프로그램으로 보내기 (CapToPath)'로 지정";

            MessageBox.Show(message, "CapToPath 안내", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
