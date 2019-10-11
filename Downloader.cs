namespace Spider
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Xml.XPath;

    using HtmlAgilityPack;

    /// <summary>
    /// Contains the spidering logic to download files.
    /// </summary>
    public class Downloader
    {
        private int maxDepth = 3;
        private string output = "output";

        private Queue<Node> uris = new Queue<Node>();
        private HashSet<string> extensions = new HashSet<string>();
        private string includeFilter = string.Empty;
        private string excludeFilter = string.Empty;
        private HashSet<string> seen = new HashSet<string>();

        /// <summary>
        /// Initializes an instance of the Downloader class.
        /// </summary>
        public Downloader()
        {
            // Load configuration.
            using (Stream file = File.OpenRead("config.xml"))
            {
                XPathDocument doc = new XPathDocument(file);
                XPathNavigator nav = doc.CreateNavigator();
                XPathNodeIterator it = nav.Select("//uri");
                foreach (XPathNavigator nav2 in it)
                {
                    uris.Enqueue(new Node(new Uri(nav2.Value, UriKind.Absolute), 1, SrcType.Page));
                }

                it = nav.Select("//ext");
                foreach (XPathNavigator nav2 in it)
                {
                    extensions.Add(nav2.Value);
                }

                string depth = nav.SelectSingleNode("//maxdepth")?.Value;
                if (!string.IsNullOrWhiteSpace(depth)) {
                    int.TryParse(depth, out maxDepth);
                }

                output = nav.SelectSingleNode("//output")?.Value;
                includeFilter = nav.SelectSingleNode("//include")?.Value;
                excludeFilter = nav.SelectSingleNode("//exclude")?.Value;
            }

            // Create output directory if it doesn't exist.
            if (!Directory.Exists(output))
            {
                Directory.CreateDirectory(output);
            }
        }

        /// <summary>
        /// Runs the spidering processing.
        /// </summary>
        public void Run()
        {
            // Loop through URLs and download.
            int activeDownloads = 0;
            while (uris.Count > 0)
            {
                // TODO: multithread
                if (uris.Count <= 0) { break; }
                Node node = uris.Dequeue();
                if (node.Target == SrcType.Media)
                {
                    string filename = Path.GetFileName(node.Uri.LocalPath);
                    if (!string.IsNullOrWhiteSpace(filename))
                    {
                        string extension = GetFileExtension(filename);
                        if (extensions.Contains(extension))
                        {
                            FileInfo info = new FileInfo(filename);
                            if (info.Exists && info.Length > 0)
                            {
                                continue;
                            }

                            WebClient client = new WebClient();
                            // TODO: Setup client with headers from root page load plus browser string etc.
                            try
                            {
                                ++activeDownloads;
                                client.DownloadFileCompleted += (s, e) => { activeDownloads--; };
                                client.DownloadFileAsync(node.Uri, Path.Combine(output, filename));
                            }
                            catch (Exception e) // e isn't used but it was useful during debugging to see what exceptions were happening.
                            {
                                string uriText = node.Uri.ToString();
                                if (uriText.StartsWith("file:"))
                                {
                                    uriText = uriText.Replace("file", "https");
                                    try
                                    {
                                        client.DownloadFileAsync(new Uri(uriText), Path.Combine(output, filename));
                                    }
                                    catch (Exception)
                                    {
                                        // Nothing.
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (node.Depth <= maxDepth)
                    {
                        int nextDepth = node.Depth + 1;
                        HtmlWeb web = new HtmlWeb();
                        try
                        {
                            HtmlDocument doc = web.Load/*FromWebAsync*/(node.Uri);
                            List<Node> links = GetLinks(doc, node.Uri, nextDepth);
                            foreach (Node link in links)
                            {
                                Console.WriteLine($"{nextDepth} - {link.Target.ToString()} - {link.Uri}");
                                uris.Enqueue(link);
                            }
                        }
                        catch (Exception)
                        {
                            // Nothing.
                        }
                    }
                }
            }

            while (activeDownloads > 0)
            {
                Thread.Sleep(1000);
            }
        }

        /// <summary>
        /// Gets the extension of a filename from within a URL string.
        /// </summary>
        /// <param name="filename">The filename from the URL, may contain query string parameters which will be stripped off.</param>
        /// <returns>The extension of the file.</returns>
        private static string GetFileExtension(string filename)
        {
            int question = filename.IndexOf('?');
            string extension = ((question > 0) ? filename.Substring(0, question) : filename);
            extension = extension.Substring(extension.LastIndexOf('.') + 1).ToLower();
            return extension;
        }

        /// <summary>
        /// Gets links from an HTML page.
        /// </summary>
        /// <param name="doc">The <see cref="HtmlDocument"/> of the HTML page to get the links from.</param>
        /// <param name="source">Source page URL.</param>
        /// <param name="depth">The link depth.</param>
        /// <returns>The links found.</returns>
        private List<Node> GetLinks(HtmlDocument doc, Uri source, int depth)
        {
            List<Node> links = new List<Node>();
            XPathNavigator navi = doc.CreateNavigator();

            string baseString = navi.Select("//base/@href")?.Current?.Value;
            bool hasBase = !string.IsNullOrWhiteSpace(baseString);
            Uri baseUri = hasBase ? new Uri(baseString) : null;

            foreach (XPathNavigator nav in navi.Select("//a/@href | //iframe/@src | //frame/@src"))
            {
                Uri uri = GetUri(source, baseUri, nav.Value);
                string uriText = uri.ToString();
                if (!uriText.StartsWith("mailto:") &&
                    !uriText.StartsWith("file:") &&
                    !uriText.StartsWith("javascript") &&
                    !uriText.Contains('#') &&
                    Regex.IsMatch(uriText, includeFilter) &&
                    (string.IsNullOrEmpty(excludeFilter) || !Regex.IsMatch(uriText, excludeFilter)) &&
                    !seen.Contains(uriText))
                {
                    string filename = Path.GetFileName(uri.LocalPath);
                    if (!string.IsNullOrWhiteSpace(filename))
                    {
                        string extension = GetFileExtension(filename);
                        if (extensions.Contains(extension))
                        {
                            links.Add(new Node(uri, depth, SrcType.Media));
                            seen.Add(uriText);
                            continue;
                        }
                    }

                    // Only include child page links if their depth will not exceed the max spidering depth.
                    if (depth <= maxDepth)
                    {
                        links.Add(new Node(uri, depth, SrcType.Page));
                        seen.Add(uriText);
                    }
                }
            }

            foreach (XPathNavigator nav in navi.Select("//img/@src | //video/@src | //source/@src"))
            {
                Uri uri = GetUri(source, baseUri, nav.Value);
                string uriText = uri.ToString();
                if (!uriText.StartsWith('{') &&
                    !uriText.StartsWith("data:") &&
                    Regex.IsMatch(uriText, includeFilter) &&
                    (string.IsNullOrEmpty(excludeFilter) || !Regex.IsMatch(uriText, excludeFilter)) &&
                    !seen.Contains(uriText))
                {
                    links.Add(new Node(uri, depth, SrcType.Media));
                    seen.Add(uriText);
                }
            }

            return links;
        }

        /// <summary>
        /// Gets a URI that should work from what may be a relative URL.
        /// </summary>
        /// <param name="source">The page source.</param>
        /// <param name="baseUri">The base URI if there was a base tag.</param>
        /// <param name="link">The link to get an absolute URI for.</param>
        /// <returns>A <see cref="Uri"/>.</returns>
        private static Uri GetUri(Uri source, Uri baseUri, string link)
        {
            Uri uri;
            link = link.Replace("file:", "https:");
            int anchor = link.IndexOf('#');
            if (anchor >= 0)
            {
                link = link.Substring(0, anchor);
            }

            if (baseUri != null)
            {
                uri = new Uri(baseUri, link);
            }
            else
            {
                if (!Uri.TryCreate(link, UriKind.Absolute, out uri))
                {
                    uri = new Uri(source, link);
                }
            }

            return uri;
        }

        /// <summary>
        /// Enumeration of link types (pages or files).
        /// </summary>
        private enum SrcType
        {
            Page,
            Media
        }

        /// <summary>
        /// Represents a node for processing.
        /// </summary>
        private class Node
        {
            public Uri Uri { get; set; }
            public int Depth { get; set; }
            public SrcType Target { get; set; }

            /// <summary>
            /// Creates a new Node.
            /// </summary>
            /// <param name="uri">The URL of the page or resource.</param>
            /// <param name="depth">Depth of spidering.</param>
            /// <param name="target">Page or Media.</param>
            public Node(Uri uri, int depth, SrcType target)
            {
                this.Uri = uri;
                this.Depth = depth;
                this.Target = target;
            }
        }
    }
}
