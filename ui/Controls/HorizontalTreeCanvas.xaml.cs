using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using BOMManager.Core;
using BOMManager.Models;

namespace BOMManager.UI.Controls
{
    public partial class HorizontalTreeCanvas : UserControl
    {
        private List<BOMTreeNode> _rootNodes = new();
        private List<BOMItem> _rawItems = new();
        private ISolidWorksService? _swService;
        private string? _rootDocTitle;
        private Point? _lastDragPoint;
        private BOMTreeNode? _selectedNode;

        // Card Drag & Drop tracking
        private Point _cardDragStartPoint;
        private BOMTreeNode? _dragCandidateNode;
        private bool _isDraggingCard;

        public event EventHandler<BOMItem>? ItemSelected;
        public event EventHandler<(BOMItem DraggedItem, BOMItem TargetParentItem)>? ItemReparented;
        public event EventHandler<BOMItem?>? CreateSubAssyRequested;
        public event EventHandler? HierarchyChanged;

        public HorizontalTreeCanvas()
        {
            InitializeComponent();
        }

        public void LoadItems(IList<BOMItem> items, ISolidWorksService swService, string? rootDocTitle = null)
        {
            _swService = swService;
            _rootDocTitle = rootDocTitle;
            _rawItems = items.ToList();

            // Build hierarchical forest with Master Root assembly wrapping
            _rootNodes = BOMTreeNode.BuildForest(_rawItems, _rootDocTitle);

            // Expand Root level, and preserve node expansion if set, or expand Sub1 if category assigned
            foreach (var root in _rootNodes)
            {
                root.IsExpanded = true;
                foreach (var child in root.Children)
                {
                    child.IsExpanded = child.Item.IsExpanded || !string.IsNullOrEmpty(child.Item.AssyCategory);
                    foreach (var sub2 in child.Children)
                    {
                        sub2.IsExpanded = sub2.Item.IsExpanded || !string.IsNullOrEmpty(sub2.Item.AssyCategory);
                    }
                }
            }

            RedrawTree();
        }

        public void RedrawTree()
        {
            if (_rootNodes.Count == 0)
            {
                linksCanvas.Children.Clear();
                nodesCanvas.Children.Clear();
                mainCanvas.Width = 600;
                mainCanvas.Height = 400;
                return;
            }

            // 1. Calculate Left-to-Right Coordinates
            var (totalWidth, totalHeight) = BOMTreeNode.CalculateHorizontalLayout(_rootNodes, 50, 50, 80, 20);
            mainCanvas.Width = Math.Max(totalWidth, 1200);
            mainCanvas.Height = Math.Max(totalHeight, 650);

            linksCanvas.Children.Clear();
            nodesCanvas.Children.Clear();

            // 2. Draw Nodes and Connection Curves
            foreach (var root in _rootNodes)
            {
                RenderSubtree(root);
            }
        }

        private void RenderSubtree(BOMTreeNode node)
        {
            // 1. Draw connecting line to parent if visible (Left-to-Right)
            if (node.Parent != null)
            {
                var curve = CreateHorizontalBezierLink(
                    node.Parent.RightConnectorX,
                    node.Parent.ConnectorY,
                    node.LeftConnectorX,
                    node.ConnectorY,
                    node.Parent.IsSubassembly ? "#3B82F6" : "#94A3B8");
                linksCanvas.Children.Add(curve);
            }

            // 2. Create and place Node Card
            var card = CreateNodeCard(node);
            Canvas.SetLeft(card, node.X);
            Canvas.SetTop(card, node.Y);
            nodesCanvas.Children.Add(card);

            // 3. Render children if expanded
            if (node.IsExpanded)
            {
                foreach (var child in node.Children)
                {
                    RenderSubtree(child);
                }
            }
        }

        private Path CreateHorizontalBezierLink(double startX, double startY, double endX, double endY, string strokeColorHex)
        {
            double midX = (startX + endX) / 2.0;

            var figure = new PathFigure
            {
                StartPoint = new Point(startX, startY),
                IsClosed = false
            };

            var segment = new BezierSegment
            {
                Point1 = new Point(midX, startY),
                Point2 = new Point(midX, endY),
                Point3 = new Point(endX, endY)
            };

            figure.Segments.Add(segment);

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            var brush = (SolidColorBrush)new BrushConverter().ConvertFrom(strokeColorHex)!;
            return new Path
            {
                Data = geometry,
                Stroke = brush,
                StrokeThickness = 2.0,
                SnapsToDevicePixels = true
            };
        }

        private FrameworkElement CreateNodeCard(BOMTreeNode node)
        {
            var item = node.Item;
            bool isHighlighted = IsSelectedOrAncestor(node);

            var cardBorder = new Border
            {
                Width = node.Width,
                Height = node.Height,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(isHighlighted ? 3.75 : (node.TreeDepth == 0 ? 3.0 : 2.25)),
                Background = (SolidColorBrush)new BrushConverter().ConvertFrom(node.NodeBackground)!,
                BorderBrush = isHighlighted ? new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)) : (SolidColorBrush)new BrushConverter().ConvertFrom(node.NodeBorderBrush)!,
                Padding = new Thickness(8, 4, 8, 4),
                SnapsToDevicePixels = true,
                UseLayoutRounding = true,
                Cursor = Cursors.Hand,
                AllowDrop = true,
                Tag = node
            };
            TextOptions.SetTextFormattingMode(cardBorder, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(cardBorder, TextRenderingMode.ClearType);
            TextOptions.SetTextHintingMode(cardBorder, TextHintingMode.Auto);
            RenderOptions.SetClearTypeHint(cardBorder, ClearTypeHint.Enabled);
            RenderOptions.SetBitmapScalingMode(cardBorder, BitmapScalingMode.HighQuality);

            var shadow = (DropShadowEffect)Resources["CardShadow"];
            cardBorder.Effect = isHighlighted ? (DropShadowEffect)Resources["HoverShadow"] : shadow;

            // Hover effects
            cardBorder.MouseEnter += (s, e) =>
            {
                if (!_isDraggingCard && !IsSelectedOrAncestor(node))
                {
                    cardBorder.Effect = (DropShadowEffect)Resources["HoverShadow"];
                    cardBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
                }
            };
            cardBorder.MouseLeave += (s, e) =>
            {
                if (!_isDraggingCard && !IsSelectedOrAncestor(node))
                {
                    cardBorder.Effect = shadow;
                    cardBorder.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFrom(node.NodeBorderBrush)!;
                }
            };

            // Drag Start tracking
            cardBorder.PreviewMouseLeftButtonDown += (s, e) =>
            {
                // Don't drag if clicking interactive controls
                if (e.OriginalSource is DependencyObject dep)
                {
                    if (FindVisualParent<ComboBox>(dep) != null ||
                        FindVisualParent<Button>(dep) != null ||
                        FindVisualParent<CheckBox>(dep) != null ||
                        FindVisualParent<TextBox>(dep) != null)
                    {
                        _dragCandidateNode = null;
                        return;
                    }
                }

                _cardDragStartPoint = e.GetPosition(this);
                _dragCandidateNode = node;
            };

            cardBorder.PreviewMouseMove += (s, e) =>
            {
                if (_dragCandidateNode == node && e.LeftButton == MouseButtonState.Pressed && !_isDraggingCard)
                {
                    Point currentPos = e.GetPosition(this);
                    Vector diff = _cardDragStartPoint - currentPos;

                    if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                        Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                    {
                        // Master root itself cannot be dragged
                        if (node.Parent == null && node.Level == 0 && _rootNodes.Count == 1)
                        {
                            return;
                        }

                        _isDraggingCard = true;
                        var data = new DataObject("BOMTreeNode", node);
                        DragDrop.DoDragDrop(cardBorder, data, DragDropEffects.Move);
                        _isDraggingCard = false;
                        _dragCandidateNode = null;
                    }
                }
            };

            cardBorder.PreviewMouseLeftButtonUp += (s, e) =>
            {
                _dragCandidateNode = null;
                _isDraggingCard = false;
            };

            // Drop Target Handling
            cardBorder.DragOver += (s, e) =>
            {
                if (e.Data.GetDataPresent("BOMTreeNode"))
                {
                    var draggedNode = e.Data.GetData("BOMTreeNode") as BOMTreeNode;
                    if (draggedNode != null &&
                        draggedNode != node &&
                        !node.IsDescendantOf(draggedNode) &&
                        (node.IsSubassembly || node.TreeDepth == 0))
                    {
                        e.Effects = DragDropEffects.Move;
                        cardBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
                        cardBorder.Background = new SolidColorBrush(Color.FromRgb(0xDB, 0xEA, 0xFE));
                        e.Handled = true;
                        return;
                    }
                }

                e.Effects = DragDropEffects.None;
                e.Handled = true;
            };

            cardBorder.DragLeave += (s, e) =>
            {
                bool isHilite = IsSelectedOrAncestor(node);
                cardBorder.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(node.NodeBackground)!;
                cardBorder.BorderBrush = isHilite ? new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)) : (SolidColorBrush)new BrushConverter().ConvertFrom(node.NodeBorderBrush)!;
            };

            cardBorder.Drop += (s, e) =>
            {
                bool isHilite = IsSelectedOrAncestor(node);
                cardBorder.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(node.NodeBackground)!;
                cardBorder.BorderBrush = isHilite ? new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)) : (SolidColorBrush)new BrushConverter().ConvertFrom(node.NodeBorderBrush)!;

                if (e.Data.GetDataPresent("BOMTreeNode"))
                {
                    var draggedNode = e.Data.GetData("BOMTreeNode") as BOMTreeNode;
                    if (draggedNode != null &&
                        draggedNode != node &&
                        !node.IsDescendantOf(draggedNode) &&
                        (node.IsSubassembly || node.TreeDepth == 0))
                    {
                        node.IsExpanded = true;
                        node.Item.IsExpanded = true;
                        ItemReparented?.Invoke(this, (draggedNode.Item, node.Item));
                        e.Handled = true;
                    }
                }
            };

            cardBorder.MouseLeftButtonDown += (s, e) =>
            {
                if (e.OriginalSource is DependencyObject dep)
                {
                    if (FindVisualParent<ComboBox>(dep) != null ||
                        FindVisualParent<Button>(dep) != null ||
                        FindVisualParent<CheckBox>(dep) != null ||
                        FindVisualParent<TextBox>(dep) != null)
                    {
                        return;
                    }
                }

                _selectedNode = node;
                UpdateSelectionVisuals();
                ItemSelected?.Invoke(this, item);
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 0: Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1: Part Name
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Row 2: Details
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 3: Bottom info

            // Row 0: Top Header (Left Badge with centered text, Sub-Assy Dropdown & Apply Checkbox, Right Isolate Button)
            var topPanel = new DockPanel { LastChildFill = true };

            // Badge (Text is vertically & horizontally centered within badge, badge is docked Left)
            var badgeBorder = new Border
            {
                Background = (SolidColorBrush)new BrushConverter().ConvertFrom(node.NodeBadgeColor)!,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 2.5, 8, 2.5),
                Margin = new Thickness(0, 0, 6, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            var badgeText = new TextBlock
            {
                Text = node.NodeTypeBadge,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            badgeBorder.Child = badgeText;
            DockPanel.SetDock(badgeBorder, Dock.Left);
            topPanel.Children.Add(badgeBorder);

            // Isolate Circle Button (🟢 / 🔴) on far Right
            var btnIsolate = new Button
            {
                Width = 20,
                Height = 20,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "SolidWorks에서 이 부품만 불투명(🟢) 강조 / 투명(🔴) 토글"
            };

            var ellipse = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = item.IsOpaque ? new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)) : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                Stroke = item.IsOpaque ? new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)) : new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                StrokeThickness = 1.5
            };
            btnIsolate.Content = ellipse;

            btnIsolate.Click += (s, e) =>
            {
                e.Handled = true;
                item.IsOpaque = !item.IsOpaque;
                ellipse.Fill = item.IsOpaque ? new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)) : new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
                ellipse.Stroke = item.IsOpaque ? new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)) : new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));

                if (_swService != null)
                {
                    _swService.SetComponentsTransparency(new List<BOMItem> { item }, _rawItems, isolateMode: true);
                }
            };

            DockPanel.SetDock(btnIsolate, Dock.Right);
            topPanel.Children.Add(btnIsolate);

            // Sub1/Sub-Assy (excluding Root) -> Assy Category Dropdown & Apply CheckBox
            if (node.TreeDepth > 0 && node.IsSubassembly)
            {
                // Apply Checkbox (placed to the left of Isolate Circle button)
                var chkApply = new CheckBox
                {
                    Content = "적용",
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 4, 0),
                    Cursor = Cursors.Hand,
                    ToolTip = "체크 시 하위 Assy/부품 트리 표시",
                    IsChecked = node.IsExpanded
                };

                chkApply.Checked += (s, e) =>
                {
                    node.IsExpanded = true;
                    RedrawTree();
                    HierarchyChanged?.Invoke(this, EventArgs.Empty);
                };

                chkApply.Unchecked += (s, e) =>
                {
                    node.IsExpanded = false;
                    RedrawTree();
                    HierarchyChanged?.Invoke(this, EventArgs.Empty);
                };

                DockPanel.SetDock(chkApply, Dock.Right);
                topPanel.Children.Add(chkApply);

                // ComboBox for Assy Category Selection
                var cboAssy = new ComboBox
                {
                    Height = 24,
                    Margin = new Thickness(4, 0, 2, 0),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.Hand,
                    ToolTip = "Assy. 종류 선택 (선택 시 하위 트리가 자동으로 펼쳐집니다)"
                };

                // ELASTOMER ASSY 하위에 있는 ASSY -> FRAME ASSY, BOTTOM COVER ASSY
                bool isUnderElastomer = false;
                var curParent = node.Parent;
                while (curParent != null)
                {
                    string pCat = (curParent.Item.AssyCategory ?? "").ToUpperInvariant();
                    string pName = (curParent.Item.PartName ?? "").ToUpperInvariant();
                    if (pCat.Contains("ELASTOMER") || pName.Contains("ELASTOMER"))
                    {
                        isUnderElastomer = true;
                        break;
                    }
                    curParent = curParent.Parent;
                }

                cboAssy.Items.Add(new ComboBoxItem { Content = "[ Assy. 선택 ]", Tag = "" });
                if (isUnderElastomer)
                {
                    cboAssy.Items.Add(new ComboBoxItem { Content = "FRAME ASSY", Tag = "FRAME Assy." });
                    cboAssy.Items.Add(new ComboBoxItem { Content = "BOTTOM COVER ASSY", Tag = "Bottom Cover Assy." });
                }
                else
                {
                    cboAssy.Items.Add(new ComboBoxItem { Content = "LID ASSY", Tag = "LID Assy" });
                    cboAssy.Items.Add(new ComboBoxItem { Content = "ELASTOMER ASSY", Tag = "Elastomer Assy." });
                    cboAssy.Items.Add(new ComboBoxItem { Content = "BSS ASSY", Tag = "BSS Assy." });
                }

                string currentCat = (item.AssyCategory ?? "").Trim();
                int selectedIdx = 0;
                for (int i = 1; i < cboAssy.Items.Count; i++)
                {
                    if (cboAssy.Items[i] is ComboBoxItem cbi)
                    {
                        string tag = (cbi.Tag as string ?? "").Trim();
                        string content = (cbi.Content as string ?? "").Trim();
                        if (!string.IsNullOrEmpty(currentCat) &&
                            (string.Equals(tag, currentCat, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(content, currentCat, StringComparison.OrdinalIgnoreCase) ||
                             tag.IndexOf(currentCat, StringComparison.OrdinalIgnoreCase) >= 0 ||
                             currentCat.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            selectedIdx = i;
                            break;
                        }
                    }
                }
                cboAssy.SelectedIndex = selectedIdx;

                cboAssy.SelectionChanged += (s, e) =>
                {
                    if (cboAssy.SelectedItem is ComboBoxItem selItem)
                    {
                        string tagVal = selItem.Tag as string ?? "";
                        if (!string.Equals(item.AssyCategory, tagVal, StringComparison.OrdinalIgnoreCase))
                        {
                            item.AssyCategory = tagVal;
                            item.CheckModified();
                            if (!string.IsNullOrEmpty(tagVal))
                            {
                                node.IsExpanded = true;
                            }
                            RedrawTree();
                            HierarchyChanged?.Invoke(this, EventArgs.Empty);
                        }
                    }
                };

                topPanel.Children.Add(cboAssy);
            }
            // Part Nodes: Root 직속 파트, ELASTOMER ASSY 하위 파트, BSS ASSY 하위 파트 등
            else if (!node.IsSubassembly)
            {
                // 상위 어셈블리 카테고리 파악
                string parentAssyCat = "";
                var curP = node.Parent;
                while (curP != null)
                {
                    string pCat = (curP.Item.AssyCategory ?? "").ToUpperInvariant();
                    string pName = (curP.Item.PartName ?? "").ToUpperInvariant();
                    if (pCat.Contains("FRAME") || pName.Contains("FRAME"))
                    {
                        parentAssyCat = "FRAME";
                        break;
                    }
                    else if (pCat.Contains("BOTTOM") || pName.Contains("BOTTOM"))
                    {
                        parentAssyCat = "BOTTOM";
                        break;
                    }
                    else if (pCat.Contains("ELASTOMER") || pName.Contains("ELASTOMER"))
                    {
                        parentAssyCat = "ELASTOMER";
                        break;
                    }
                    else if (pCat.Contains("BSS") || pName.Contains("BSS"))
                    {
                        parentAssyCat = "BSS";
                        break;
                    }
                    else if (pCat.Contains("LID") || pName.Contains("LID"))
                    {
                        parentAssyCat = "LID";
                        break;
                    }
                    else if (curP.TreeDepth == 0)
                    {
                        parentAssyCat = "ROOT";
                        break;
                    }
                    curP = curP.Parent;
                }

                var partOptions = new List<(string Display, string Tag)>();
                if (parentAssyCat == "ELASTOMER")
                {
                    partOptions.Add(("[ 선택 ]", ""));
                    partOptions.Add(("ELASTOMER", "ELASTOMER"));
                    partOptions.Add(("FRAME BOLT", "FRAME bolt"));
                }
                else if (parentAssyCat == "FRAME")
                {
                    partOptions.Add(("[ 선택 ]", ""));
                    partOptions.Add(("FRAME", "FRAME"));
                    partOptions.Add(("FRAME BUSH", "FRAME BUSH"));
                    partOptions.Add(("BLOCK", "BLOCK"));
                }
                else if (parentAssyCat == "BOTTOM")
                {
                    partOptions.Add(("[ 선택 ]", ""));
                    partOptions.Add(("BOTTOM COVER", "Bottom Cover"));
                }
                else if (parentAssyCat == "BSS")
                {
                    partOptions.Add(("[ 선택 ]", ""));
                    partOptions.Add(("BSS BASE", "BSS BASE"));
                    partOptions.Add(("INSULATION FILM", "INSULATION FILM"));
                }
                else if (parentAssyCat == "LID")
                {
                    partOptions.Add(("[ 선택 ]", ""));
                    partOptions.Add(("COVER", "Cover"));
                    partOptions.Add(("PUSHER", "Pusher"));
                    partOptions.Add(("PUSHER BOLT", "Pusher bolt"));
                    partOptions.Add(("PUSHER SPRING", "Pusher spring"));
                    partOptions.Add(("LID BOLT", "Lid bolt"));
                }
                else if (parentAssyCat == "ROOT" || node.TreeDepth == 1)
                {
                    partOptions.Add(("[ 선택 ]", ""));
                    partOptions.Add(("FRAME BOLT", "FRAME bolt"));
                    partOptions.Add(("DEVICE", "DEVICE"));
                    partOptions.Add(("PCB", "PCB"));
                }

                if (partOptions.Count > 0)
                {
                    var chkPartApply = new CheckBox
                    {
                        Content = "적용",
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 4, 0),
                        Cursor = Cursors.Hand,
                        ToolTip = "체크 시 선택된 항목 적용",
                        IsChecked = !string.IsNullOrEmpty(item.AssyCategory)
                    };

                    var cboPartCat = new ComboBox
                    {
                        Height = 24,
                        Margin = new Thickness(4, 0, 2, 0),
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Cursor = Cursors.Hand,
                        ToolTip = "부품 카테고리 선택"
                    };

                    foreach (var opt in partOptions)
                    {
                        cboPartCat.Items.Add(new ComboBoxItem { Content = opt.Display, Tag = opt.Tag });
                    }

                    string currentCat = (item.AssyCategory ?? "").Trim();
                    int selectedIdx = 0;
                    for (int i = 1; i < cboPartCat.Items.Count; i++)
                    {
                        if (cboPartCat.Items[i] is ComboBoxItem cbi)
                        {
                            string tag = (cbi.Tag as string ?? "").Trim();
                            string content = (cbi.Content as string ?? "").Trim();
                            if (!string.IsNullOrEmpty(currentCat) &&
                                (string.Equals(tag, currentCat, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(content, currentCat, StringComparison.OrdinalIgnoreCase) ||
                                 tag.IndexOf(currentCat, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 currentCat.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                selectedIdx = i;
                                break;
                            }
                        }
                    }
                    cboPartCat.SelectedIndex = selectedIdx;

                    cboPartCat.SelectionChanged += (s, e) =>
                    {
                        if (cboPartCat.SelectedItem is ComboBoxItem selItem)
                        {
                            string tagVal = selItem.Tag as string ?? "";
                            if (!string.Equals(item.AssyCategory, tagVal, StringComparison.OrdinalIgnoreCase))
                            {
                                item.AssyCategory = tagVal;
                                item.CheckModified();
                                RedrawTree();
                                HierarchyChanged?.Invoke(this, EventArgs.Empty);
                            }
                        }
                    };

                    chkPartApply.Checked += (s, e) =>
                    {
                        if (cboPartCat.SelectedItem is ComboBoxItem selItem && !string.IsNullOrEmpty(selItem.Tag as string))
                        {
                            item.AssyCategory = selItem.Tag as string ?? "";
                        }
                        else if (cboPartCat.Items.Count > 1)
                        {
                            cboPartCat.SelectedIndex = 1;
                            item.AssyCategory = (cboPartCat.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                        }
                        item.CheckModified();
                        RedrawTree();
                        HierarchyChanged?.Invoke(this, EventArgs.Empty);
                    };

                    chkPartApply.Unchecked += (s, e) =>
                    {
                        item.AssyCategory = "";
                        cboPartCat.SelectedIndex = 0;
                        item.CheckModified();
                        RedrawTree();
                        HierarchyChanged?.Invoke(this, EventArgs.Empty);
                    };

                    DockPanel.SetDock(chkPartApply, Dock.Right);
                    topPanel.Children.Add(chkPartApply);
                    topPanel.Children.Add(cboPartCat);
                }
            }

            Grid.SetRow(topPanel, 0);
            mainGrid.Children.Add(topPanel);

            // Row 1: Part Name (Left-aligned under header)
            var txtPartName = new TextBlock
            {
                Text = item.PartName,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextAlignment = TextAlignment.Left,
                Margin = new Thickness(2, 3, 2, 1),
                ToolTip = item.PartName
            };
            Grid.SetRow(txtPartName, 1);
            mainGrid.Children.Add(txtPartName);

            // Row 2: Middle Details (DrawingNo, Material, Explanation)
            var detailsPanel = new StackPanel
            {
                Margin = new Thickness(2, 1, 2, 1),
                VerticalAlignment = VerticalAlignment.Center
            };

            if (!string.IsNullOrEmpty(item.DrawingNo))
            {
                var txtDrawing = new TextBlock
                {
                    Text = $"도번: {item.DrawingNo}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0.5, 0, 0.5)
                };
                detailsPanel.Children.Add(txtDrawing);
            }

            if (!string.IsNullOrEmpty(item.Material))
            {
                var txtMat = new TextBlock
                {
                    Text = $"재질: {item.Material}",
                    FontSize = 9.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0.5, 0, 0.5)
                };
                detailsPanel.Children.Add(txtMat);
            }

            if (!string.IsNullOrEmpty(item.Explanation))
            {
                var txtExp = new TextBlock
                {
                    Text = $"설명: {item.Explanation}",
                    FontSize = 9.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0.5, 0, 0.5)
                };
                detailsPanel.Children.Add(txtExp);
            }

            Grid.SetRow(detailsPanel, 2);
            mainGrid.Children.Add(detailsPanel);

            // Row 3: Bottom Info (Q'TY badge, Modified indicator, Expand/Collapse button)
            var bottomPanel = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 1, 0, 0) };

            var leftInfoPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var txtQty = new TextBlock
            {
                Text = $"Q'TY: {item.Qty}",
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                VerticalAlignment = VerticalAlignment.Center
            };
            leftInfoPanel.Children.Add(txtQty);

            if (item.IsCommonPart)
            {
                var commonBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(6, 0, 0, 0),
                    Child = new TextBlock
                    {
                        Text = "공용",
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09))
                    }
                };
                leftInfoPanel.Children.Add(commonBadge);
            }

            if (item.IsModified)
            {
                var modBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(6, 0, 0, 0),
                    Child = new TextBlock
                    {
                        Text = "수정됨",
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C))
                    }
                };
                leftInfoPanel.Children.Add(modBadge);
            }

            DockPanel.SetDock(leftInfoPanel, Dock.Left);
            bottomPanel.Children.Add(leftInfoPanel);

            // Expand/Collapse Button for Subassemblies
            if (node.HasChildren)
            {
                var btnToggle = new Button
                {
                    Content = $"{node.ExpandToggleGlyph} {node.Children.Count}개",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = node.IsExpanded ? new SolidColorBrush(Color.FromRgb(0x1E, 0x40, 0xAF)) : new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06)),
                    Background = node.IsExpanded ? new SolidColorBrush(Color.FromRgb(0xDB, 0xEA, 0xFE)) : new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)),
                    BorderBrush = node.IsExpanded ? new SolidColorBrush(Color.FromRgb(0x93, 0xC5, 0xFD)) : new SolidColorBrush(Color.FromRgb(0xFC, 0xD3, 0x4D)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6, 2, 6, 2),
                    Height = 20,
                    Cursor = Cursors.Hand,
                    ToolTip = node.IsExpanded ? "하위 부품 접기" : "하위 부품 펼치기"
                };

                btnToggle.Click += (s, e) =>
                {
                    e.Handled = true;
                    node.IsExpanded = !node.IsExpanded;
                    RedrawTree();
                    HierarchyChanged?.Invoke(this, EventArgs.Empty);
                };

                DockPanel.SetDock(btnToggle, Dock.Right);
                bottomPanel.Children.Add(btnToggle);
            }

            Grid.SetRow(bottomPanel, 3);
            mainGrid.Children.Add(bottomPanel);

            cardBorder.Child = mainGrid;
            return cardBorder;
        }

        private void BtnCreateSubAssy_Click(object sender, RoutedEventArgs e)
        {
            var targetParent = _selectedNode?.Item ?? _rootNodes.FirstOrDefault()?.Item;
            CreateSubAssyRequested?.Invoke(this, targetParent);
        }

        #region Zoom & Pan Handling

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                double zoomDelta = e.Delta > 0 ? 0.1 : -0.1;
                ApplyZoom(treeScale.ScaleX + zoomDelta);
            }
        }

        private void ApplyZoom(double newScale)
        {
            newScale = Math.Max(0.4, Math.Min(2.5, newScale));
            treeScale.ScaleX = newScale;
            treeScale.ScaleY = newScale;
            btnZoomReset.Content = $"⛶ {Math.Round(newScale * 100)}%";
        }

        private void UpdateSelectionVisuals()
        {
            foreach (UIElement child in nodesCanvas.Children)
            {
                if (child is Border b && b.Tag is BOMTreeNode n)
                {
                    bool isHighlighted = IsSelectedOrAncestor(n);
                    b.BorderThickness = new Thickness(isHighlighted ? 3.75 : (n.TreeDepth == 0 ? 3.0 : 2.25));
                    b.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(n.NodeBackground)!;
                    b.BorderBrush = isHighlighted ? new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)) : (SolidColorBrush)new BrushConverter().ConvertFrom(n.NodeBorderBrush)!;
                    b.Effect = isHighlighted ? (DropShadowEffect)Resources["HoverShadow"] : (DropShadowEffect)Resources["CardShadow"];
                }
            }
        }

        private bool IsSelectedOrAncestor(BOMTreeNode node)
        {
            if (_selectedNode == null) return false;
            if (_selectedNode == node) return true;
            return _selectedNode.IsDescendantOf(node);
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent) return parent;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Only pan canvas if clicking on empty background or using middle button
            if (e.ChangedButton == MouseButton.Middle ||
                (e.ChangedButton == MouseButton.Left && (e.OriginalSource == mainCanvas || e.OriginalSource == linksCanvas || e.OriginalSource == nodesCanvas || e.OriginalSource == scrollViewer)))
            {
                _lastDragPoint = e.GetPosition(scrollViewer);
                mainCanvas.CaptureMouse();
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_lastDragPoint.HasValue && (e.LeftButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed))
            {
                Point currentPos = e.GetPosition(scrollViewer);
                double deltaX = _lastDragPoint.Value.X - currentPos.X;
                double deltaY = _lastDragPoint.Value.Y - currentPos.Y;

                scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset + deltaX);
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + deltaY);

                _lastDragPoint = currentPos;
            }
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _lastDragPoint = null;
            if (mainCanvas.IsMouseCaptured)
            {
                mainCanvas.ReleaseMouseCapture();
            }
        }

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            ApplyZoom(treeScale.ScaleX + 0.15);
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            ApplyZoom(treeScale.ScaleX - 0.15);
        }

        private void BtnZoomReset_Click(object sender, RoutedEventArgs e)
        {
            ApplyZoom(1.0);
        }

        private void BtnTreeExpandAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var root in _rootNodes)
            {
                root.SetExpandedRecursive(true);
            }
            RedrawTree();
        }

        private void BtnTreeCollapseAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var root in _rootNodes)
            {
                root.IsExpanded = false;
            }
            RedrawTree();
        }

        #endregion
    }
}
