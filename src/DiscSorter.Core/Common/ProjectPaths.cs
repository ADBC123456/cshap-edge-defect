using System;
using System.IO;

namespace DiscSorter.Core.Common
{
    /// <summary>
    /// 找工程根目录（含 DiscSorter.sln 的文件夹），用来定位默认的 halcon\ 和 data\ 文件夹。
    /// </summary>
    public static class ProjectPaths
    {
        /// <summary>从程序所在目录开始一级一级往上找，找不到返回 null。</summary>
        public static string FindRoot()
        {
            return FindRoot(AppContext.BaseDirectory);
        }

        public static string FindRoot(string startDirectory)
        {
            DirectoryInfo directory = new DirectoryInfo(startDirectory);
            while (directory != null)
            {
                string solutionFile = Path.Combine(directory.FullName, "DiscSorter.sln");
                if (File.Exists(solutionFile))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            return null;
        }
    }
}
