using System;
using System.Collections.Generic;
using System.IO;
using HalconDotNet;

namespace DiscSorter.Core.Vision
{
    /// <summary>
    /// 图片文件夹回放：打开一个文件夹（或一张图及其所在文件夹），按文件名顺序上一张、下一张循环浏览。
    /// 现在用它代替相机；以后接相机时另写一个类，界面和分拣流程不用改。
    /// </summary>
    public class ImageFolder : IImageSource
    {
        private readonly List<string> files = new List<string>();

        /// <summary>当前文件夹，没打开时为 null。</summary>
        public string Folder { get; private set; }

        /// <summary>文件夹里的全部图片路径（按文件名排序）。</summary>
        public List<string> Files
        {
            get { return files; }
        }

        /// <summary>当前是第几张（从 0 开始），没有图片时为 -1。</summary>
        public int Index { get; private set; }

        public ImageFolder()
        {
            Index = -1;
        }

        /// <summary>当前图片路径，没有图片时为 null。</summary>
        public string CurrentPath
        {
            get
            {
                if (Index >= 0 && Index < files.Count)
                {
                    return files[Index];
                }
                return null;
            }
        }

        /// <summary>例如 "Q_1.jpg · 3 / 11"。</summary>
        public string Description
        {
            get
            {
                if (CurrentPath == null)
                {
                    return "未打开图片";
                }
                return Path.GetFileName(CurrentPath) + " · " + (Index + 1) + " / " + files.Count;
            }
        }

        /// <summary>打开文件夹，定位到第一张图。</summary>
        public void OpenFolder(string folder)
        {
            if (!Directory.Exists(folder))
            {
                throw new DirectoryNotFoundException("文件夹不存在：" + folder);
            }

            files.Clear();
            foreach (string file in Directory.GetFiles(folder))
            {
                if (IsImageFile(file))
                {
                    files.Add(file);
                }
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);

            Folder = folder;
            if (files.Count > 0)
            {
                Index = 0;
            }
            else
            {
                Index = -1;
            }
        }

        /// <summary>打开一张图：载入它所在的整个文件夹，并定位到这张图。</summary>
        public void OpenFile(string file)
        {
            string fullPath = Path.GetFullPath(file);
            OpenFolder(Path.GetDirectoryName(fullPath));
            for (int i = 0; i < files.Count; i++)
            {
                if (string.Equals(Path.GetFullPath(files[i]), fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    Index = i;
                    return;
                }
            }
        }

        /// <summary>下一张，到最后一张后回到第一张。</summary>
        public void MoveNext()
        {
            if (files.Count > 0)
            {
                Index = (Index + 1) % files.Count;
            }
        }

        /// <summary>上一张，到第一张后跳到最后一张。</summary>
        public void MovePrevious()
        {
            if (files.Count > 0)
            {
                Index = (Index - 1 + files.Count) % files.Count;
            }
        }

        /// <summary>取当前图片路径，并前进一张（分拣流程逐件取图用）。没有图片时返回 null。</summary>
        public string TakeNext()
        {
            string path = CurrentPath;
            MoveNext();
            return path;
        }

        // ---------------- IImageSource ----------------

        public string SourceName
        {
            get
            {
                if (Folder == null)
                {
                    return "图片文件夹（未打开）";
                }
                return "图片文件夹 " + Folder;
            }
        }

        public bool IsReady
        {
            get { return files.Count > 0; }
        }

        /// <summary>按顺序取下一张图片（到最后一张后回到第一张）。</summary>
        public HImage Grab(out string imagePath)
        {
            imagePath = TakeNext();
            if (imagePath == null)
            {
                throw new InvalidOperationException("图片文件夹里没有图片");
            }
            return ReadImage(imagePath);
        }

        /// <summary>用 HALCON 读一张图。</summary>
        public static HImage ReadImage(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("图片不存在", path);
            }
            return new HImage(path);
        }

        private static bool IsImageFile(string file)
        {
            string extension = Path.GetExtension(file).ToLower();
            return extension == ".jpg" || extension == ".jpeg" || extension == ".png"
                || extension == ".bmp" || extension == ".tif" || extension == ".tiff";
        }
    }
}
