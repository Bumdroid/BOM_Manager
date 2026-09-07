using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BOMManager.Models
{
    public class BOMTreeNode : INotifyPropertyChanged
    {
        public BOMItem Item { get; }
        public BOMTreeNode? Parent { get; set; }
        public ObservableCollection<BOMTreeNode> Children { get; } = new();

        private bool _isExpanded = true;
        private double _x;
        private double _y;
        private double _width = 380; // 380px for spacious drawing number input box and content
        private double _height = 122; // 122px for enlarged drawing number input and material dropdown

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasChildrenAndCollapsed));
                    OnPropertyChanged(nameof(ExpandToggleGlyph));
                    OnPropertyChanged(nameof(IsApplied));
                    OnPropertyChanged(nameof(NodeBackground));
                    OnPropertyChanged(nameof(NodeBorderBrush));
                }
            }
        }

        public double X
        {
            get => _x;
            set
            {
                if (Math.Abs(_x - value) > 0.001)
                {
                    _x = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RightConnectorX));
                    OnPropertyChanged(nameof(LeftConnectorX));
                }
            }
        }

        public double Y
        {
            get => _y;
            set
            {
                if (Math.Abs(_y - value) > 0.001)
                {
                    _y = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ConnectorY));
                }
            }
        }

        public double Width
        {
            get => _width;
            set
            {
                if (Math.Abs(_width - value) > 0.001)
                {
                    _width = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RightConnectorX));
                }
            }
        }

        public double Height
        {
            get => _height;
            set
            {
                if (Math.Abs(_height - value) > 0.001)
                {
                    _height = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ConnectorY));
                }
            }
        }

        public double LeftConnectorX => _x;
        public double RightConnectorX => _x + _width;
        public double TopConnectorX => _x + (_width / 2.0);
        public double BottomConnectorX => _x + (_width / 2.0);
        public double TopConnectorY => _y;
        public double BottomConnectorY => _y + _height;
        public double ConnectorY => _y + (_height / 2.0);

        public bool IsSubassembly => Item.IsSubassembly;
        public bool HasChildren => Children.Count > 0;
        public bool HasChildrenAndCollapsed => HasChildren && !_isExpanded;
        public int Level => Item.Level;

        public int TreeDepth
        {
            get
            {
                int d = 0;
                var cur = Parent;
                while (cur != null)
                {
                    d++;
                    cur = cur.Parent;
                }
                return d;
            }
        }

        public string ExpandToggleGlyph => _isExpanded ? "▼" : "▶";

        public string NodeTypeBadge
        {
            get
            {
                if (TreeDepth == 0 && IsSubassembly) return "Root Assy.";
                if (IsSubassembly) return $"Sub{TreeDepth} Assy.";
                return "PART";
            }
        }

        public bool IsApproved
        {
            get => TreeDepth == 0 || Item.IsApproved;
            set
            {
                Item.IsApproved = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsApplied));
                OnPropertyChanged(nameof(NodeBackground));
                OnPropertyChanged(nameof(NodeBorderBrush));
            }
        }

        public bool IsApplied
        {
            get
            {
                if (TreeDepth == 0) return true;
                if (IsSubassembly)
                {
                    return Item.IsApproved && !string.IsNullOrEmpty(Item.AssyCategory);
                }
                return !string.IsNullOrEmpty(Item.AssyCategory);
            }
        }

        public string NodeBadgeColor
        {
            get
            {
                if (TreeDepth == 0 && IsSubassembly) return "#059669"; // Green for Root
                if (IsSubassembly) return "#0D9488"; // Teal
                return "#475569"; // Slate
            }
        }

        public string NodeBorderBrush
        {
            get
            {
                if (TreeDepth == 0 && IsSubassembly) return "#10B981"; // Green for Root
                if (IsApplied) return "#10B981"; // Green for Applied
                return "#CBD5E1"; // Neutral Gray for Unapplied
            }
        }

        public string NodeBackground
        {
            get
            {
                if (TreeDepth == 0 && IsSubassembly) return "#ECFDF5"; // Soft Green for Root
                if (IsApplied) return "#ECFDF5"; // Soft Green for Applied
                return "#FFFFFF"; // White for Unapplied
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));

        public BOMTreeNode(BOMItem item, BOMTreeNode? parent = null)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            Parent = parent;
        }

        // 트리 계층 구축 (Flat List -> Hierarchical Forest with Master Root Node)
        public static List<BOMTreeNode> BuildForest(IList<BOMItem> flatItems, string? rootAssemblyTitle = null)
        {
            var roots = new List<BOMTreeNode>();
            if (flatItems == null || flatItems.Count == 0) return roots;

            int level0Count = 0;
            foreach (var item in flatItems)
            {
                if (item.Level == 0) level0Count++;
            }

            bool hasExplicitSingleRoot = level0Count == 1 && flatItems[0].Level == 0 && flatItems[0].IsSubassembly;

            BOMTreeNode? masterRootNode = null;
            if (!hasExplicitSingleRoot)
            {
                string title = !string.IsNullOrEmpty(rootAssemblyTitle) ? System.IO.Path.GetFileName(rootAssemblyTitle) : "TOTAL_ASSEMBLY.SLDASM";
                var rootItem = new BOMItem(0, title, isSubassembly: true, level: 0, remark: "Top-Level Total Assembly");
                masterRootNode = new BOMTreeNode(rootItem);
                roots.Add(masterRootNode);
            }

            var stack = new Stack<BOMTreeNode>();
            if (masterRootNode != null)
            {
                stack.Push(masterRootNode);
            }

            foreach (var item in flatItems)
            {
                var node = new BOMTreeNode(item);
                int currentEffectiveLevel = masterRootNode != null ? node.Level + 1 : node.Level;

                while (stack.Count > (masterRootNode != null ? 1 : 0))
                {
                    int topLevel = masterRootNode != null ? stack.Peek().Level + 1 : stack.Peek().Level;
                    if (topLevel >= currentEffectiveLevel)
                    {
                        stack.Pop();
                    }
                    else
                    {
                        break;
                    }
                }

                if (stack.Count > 0)
                {
                    var parent = stack.Peek();
                    node.Parent = parent;
                    parent.Children.Add(node);
                }
                else
                {
                    roots.Add(node);
                }

                if (node.IsSubassembly)
                {
                    stack.Push(node);
                }
            }

            return roots;
        }

        // 순환 이동 방지 (자신 또는 자신의 하위 자손인지 확인)
        public bool IsDescendantOf(BOMTreeNode potentialAncestor)
        {
            if (potentialAncestor == null) return false;
            var current = this.Parent;
            while (current != null)
            {
                if (current == potentialAncestor) return true;
                current = current.Parent;
            }
            return false;
        }

        // 상->하(Top-to-Bottom Vertical) 노드 좌표 자동 산출 (Vertical Layout)
        public static (double TotalWidth, double TotalHeight) CalculateVerticalLayout(
            IList<BOMTreeNode> roots,
            double startX = 50,
            double startY = 40,
            double horizontalGap = 24, // 형제 노드 간 수평 간격
            double verticalGap = 70)   // 상하 부모/자식 간격
        {
            double currentX = startX;
            double maxY = startY;

            foreach (var root in roots)
            {
                LayoutSubtreeVertical(root, ref currentX, startY, horizontalGap, verticalGap, ref maxY);
                currentX += horizontalGap;
            }

            double totalWidth = Math.Max(1200, currentX + 60);
            double totalHeight = Math.Max(700, maxY + 100);
            return (totalWidth, totalHeight);
        }

        private static void LayoutSubtreeVertical(
            BOMTreeNode node,
            ref double currentX,
            double currentY,
            double horizontalGap,
            double verticalGap,
            ref double maxY)
        {
            node.Y = currentY;
            if (node.Y + node.Height > maxY) maxY = node.Y + node.Height;

            if (!node.IsExpanded || node.Children.Count == 0)
            {
                node.X = currentX;
                currentX += node.Width + horizontalGap;
                return;
            }

            double firstChildX = currentX;
            double nextY = currentY + node.Height + verticalGap;

            foreach (var child in node.Children)
            {
                LayoutSubtreeVertical(child, ref currentX, nextY, horizontalGap, verticalGap, ref maxY);
            }

            double lastChildX = node.Children[node.Children.Count - 1].X;

            // 부모 노드를 자식 노드들의 수평 중심에 배치
            node.X = (firstChildX + lastChildX) / 2.0;

            if (node.X + node.Width + horizontalGap > currentX)
            {
                currentX = node.X + node.Width + horizontalGap;
            }
        }

        // 좌->우(Horizontal) 노드 좌표 자동 산출 (호환성 유지)
        public static (double TotalWidth, double TotalHeight) CalculateHorizontalLayout(
            IList<BOMTreeNode> roots,
            double startX = 40,
            double startY = 40,
            double horizontalGap = 80,
            double verticalGap = 18)
        {
            double currentY = startY;
            double maxX = startX;

            foreach (var root in roots)
            {
                LayoutSubtree(root, startX, ref currentY, horizontalGap, verticalGap, ref maxX);
                currentY += verticalGap;
            }

            double totalHeight = Math.Max(500, currentY + 60);
            double totalWidth = Math.Max(1200, maxX + 360);
            return (totalWidth, totalHeight);
        }

        private static void LayoutSubtree(
            BOMTreeNode node,
            double currentX,
            ref double currentY,
            double horizontalGap,
            double verticalGap,
            ref double maxX)
        {
            node.X = currentX;
            if (node.X + node.Width > maxX) maxX = node.X + node.Width;

            if (!node.IsExpanded || node.Children.Count == 0)
            {
                node.Y = currentY;
                currentY += node.Height + verticalGap;
                return;
            }

            double firstChildY = currentY;
            double nextX = currentX + node.Width + horizontalGap;

            foreach (var child in node.Children)
            {
                LayoutSubtree(child, nextX, ref currentY, horizontalGap, verticalGap, ref maxX);
            }

            double lastChildY = node.Children[node.Children.Count - 1].Y;
            node.Y = (firstChildY + lastChildY) / 2.0;

            if (node.Y + node.Height + verticalGap > currentY)
            {
                currentY = node.Y + node.Height + verticalGap;
            }
        }

        // 모든 브랜치 재귀 펼치기/접기
        public void SetExpandedRecursive(bool expanded)
        {
            IsExpanded = expanded;
            foreach (var child in Children)
            {
                child.SetExpandedRecursive(expanded);
            }
        }
    }
}
