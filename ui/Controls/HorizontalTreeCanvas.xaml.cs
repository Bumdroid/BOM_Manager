using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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

        private BomProcessStep _currentStep = BomProcessStep.Step1Assy;
        public BomProcessStep CurrentStep
        {
            get => _currentStep;
            set
            {
                if (_currentStep != value)
                {
                    _currentStep = value;
                    UpdateToolbarForStep();
                    RedrawTree();
                }
            }
        }

        private void UpdateToolbarForStep()
        {
            if (_currentStep == BomProcessStep.Step2DrawingNo)
            {
                btnCreateSubAssy.Visibility = Visibility.Collapsed;
                btnApplyToFile.Visibility = Visibility.Collapsed;
                btnDevTemp.Visibility = Visibility.Collapsed;
            }
            else
            {
                btnCreateSubAssy.Visibility = Visibility.Visible;
                btnApplyToFile.Visibility = Visibility.Visible;
                btnDevTemp.Visibility = Visibility.Visible;
            }
        }

        public event EventHandler<BOMItem>? ItemSelected;
        public event EventHandler<(BOMItem DraggedItem, BOMItem TargetParentItem)>? ItemReparented;
        public event EventHandler<BOMItem?>? CreateSubAssyRequested;
        public event EventHandler? ApplyToFileRequested;
        public event EventHandler? DevTempRequested;
        public event EventHandler? HierarchyChanged;

        public HorizontalTreeCanvas()
        {
            InitializeComponent();
        }

        private bool _hasInitiallyLoaded = false;

        public void ResetInitialLoadState()
        {
            _hasInitiallyLoaded = false;
        }

        public void LoadItems(IList<BOMItem> items, ISolidWorksService swService, string? rootDocTitle = null, bool forceInitialCollapse = false)
        {
            _swService = swService;
            _rootDocTitle = rootDocTitle;

            // 기존에 열려있던 노드들의 펼침(승인) 상태 맵 캡처
            var expansionMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (!_hasInitiallyLoaded && !forceInitialCollapse)
            {
                bool anyExpanded = items.Any(it => it.Level > 0 && it.IsExpanded);
                if (anyExpanded)
                {
                    _hasInitiallyLoaded = true;
                }
            }

            if (_hasInitiallyLoaded && !forceInitialCollapse)
            {
                if (_rawItems != null)
                {
                    foreach (var itm in _rawItems)
                    {
                        string key = GetItemKey(itm);
                        if (!string.IsNullOrEmpty(key)) expansionMap[key] = itm.IsExpanded;
                    }
                }
                foreach (var itm in items)
                {
                    string key = GetItemKey(itm);
                    if (!string.IsNullOrEmpty(key) && itm.IsExpanded)
                    {
                        expansionMap[key] = true;
                    }
                }
            }

            _rawItems = items.ToList();

            // Build hierarchical forest with Master Root assembly wrapping
            _rootNodes = BOMTreeNode.BuildForest(_rawItems, _rootDocTitle);

            if (!_hasInitiallyLoaded || forceInitialCollapse)
            {
                // 최초 진입 시: Root만 확장하고 Sub1 및 그 하위는 기본 접힘(미승인) 처리
                foreach (var root in _rootNodes)
                {
                    root.IsExpanded = true;
                    if (root.Item != null) root.Item.IsExpanded = true;
                    foreach (var child in root.Children)
                    {
                        CollapseAllDescendants(child);
                    }
                }
                _hasInitiallyLoaded = true;
            }
            else
            {
                // 기존 승인/펼침 상태 완벽 복원
                foreach (var root in _rootNodes)
                {
                    root.IsExpanded = true;
                    if (root.Item != null) root.Item.IsExpanded = true;
                    RestoreExpansionRecursive(root, expansionMap);
                }
            }

            RedrawTree();
        }

        private static string GetItemKey(BOMItem itm)
        {
            if (!string.IsNullOrEmpty(itm.FilePath)) return itm.FilePath;
            if (!string.IsNullOrEmpty(itm.FileName)) return itm.FileName;
            return $"{itm.PartName}_{itm.Level}";
        }

        private static void RestoreExpansionRecursive(BOMTreeNode node, Dictionary<string, bool> expansionMap)
        {
            if (node.TreeDepth > 0 && node.Item != null)
            {
                string key = GetItemKey(node.Item);
                if (expansionMap.TryGetValue(key, out bool wasExpanded))
                {
                    node.IsExpanded = wasExpanded;
                    node.Item.IsExpanded = wasExpanded;
                }
                else
                {
                    node.IsExpanded = node.Item.IsExpanded;
                }
            }
            foreach (var child in node.Children)
            {
                RestoreExpansionRecursive(child, expansionMap);
            }
        }

        private static void CollapseAllDescendants(BOMTreeNode node)
        {
            node.IsExpanded = false;
            if (node.Item != null) node.Item.IsExpanded = false;
            foreach (var child in node.Children)
            {
                CollapseAllDescendants(child);
            }
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

        private static UIElement CreateEyeIcon(bool isOpen)
        {
            var canvas = new Canvas
            {
                Width = 16,
                Height = 16,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                LayoutTransform = new ScaleTransform(1.2, 1.2)
            };

            var blackBrush = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));

            if (isOpen)
            {
                // 눈 뜬 눈알 (Black Open Eye - 완전히 검은 눈동자)
                var eyeOutline = new Path
                {
                    Stroke = blackBrush,
                    StrokeThickness = 1.5,
                    Fill = Brushes.White,
                    Data = Geometry.Parse("M 1.5,8 C 4,3 12,3 14.5,8 C 12,13 4,13 1.5,8 Z")
                };

                var pupil = new Ellipse
                {
                    Width = 5.5,
                    Height = 5.5,
                    Fill = blackBrush
                };
                Canvas.SetLeft(pupil, 5.25);
                Canvas.SetTop(pupil, 5.25);

                canvas.Children.Add(eyeOutline);
                canvas.Children.Add(pupil);
            }
            else
            {
                // 눈 감은 눈알 (Set B: Black Curved Line with Lashes)
                var eyeClosed = new Path
                {
                    Stroke = blackBrush,
                    StrokeThickness = 1.6,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Data = Geometry.Parse("M 2,6.5 C 4.5,11.5 11.5,11.5 14,6.5")
                };

                var lash1 = new Path
                {
                    Stroke = blackBrush,
                    StrokeThickness = 1.3,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Data = Geometry.Parse("M 3,8 L 1.5,10.5")
                };
                var lash2 = new Path
                {
                    Stroke = blackBrush,
                    StrokeThickness = 1.3,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Data = Geometry.Parse("M 5.5,9.8 L 4.5,12.8")
                };
                var lash3 = new Path
                {
                    Stroke = blackBrush,
                    StrokeThickness = 1.3,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Data = Geometry.Parse("M 8,10.5 L 8,13.8")
                };
                var lash4 = new Path
                {
                    Stroke = blackBrush,
                    StrokeThickness = 1.3,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Data = Geometry.Parse("M 10.5,9.8 L 11.5,12.8")
                };
                var lash5 = new Path
                {
                    Stroke = blackBrush,
                    StrokeThickness = 1.3,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Data = Geometry.Parse("M 13,8 L 14.5,10.5")
                };

                canvas.Children.Add(eyeClosed);
                canvas.Children.Add(lash1);
                canvas.Children.Add(lash2);
                canvas.Children.Add(lash3);
                canvas.Children.Add(lash4);
                canvas.Children.Add(lash5);
            }

            return canvas;
        }

        private FrameworkElement CreateNodeCard(BOMTreeNode node)
        {
            var item = node.Item;
            bool isHighlighted = IsSelectedOrAncestor(node);

            var cardContainer = new Grid
            {
                Width = node.Width,
                Height = node.Height,
                Tag = node,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };

            var shadowBorder = new Border
            {
                Width = node.Width,
                Height = node.Height,
                CornerRadius = new CornerRadius(6),
                Background = Brushes.White,
                Effect = isHighlighted ? (DropShadowEffect)Resources["HoverShadow"] : (DropShadowEffect)Resources["CardShadow"],
                IsHitTestVisible = false
            };
            cardContainer.Children.Add(shadowBorder);

            var cardBorder = new Border
            {
                Width = node.Width,
                Height = node.Height,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(isHighlighted ? 3.75 : (node.TreeDepth == 0 ? 3.0 : 2.25)),
                Background = (SolidColorBrush)new BrushConverter().ConvertFrom(GetNodeBackground(node))!,
                BorderBrush = isHighlighted ? new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)) : (SolidColorBrush)new BrushConverter().ConvertFrom(GetNodeBorderBrush(node))!,
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
            cardContainer.Children.Add(cardBorder);

            var shadow = (DropShadowEffect)Resources["CardShadow"];
            var hoverShadow = (DropShadowEffect)Resources["HoverShadow"];

            // Hover effects
            cardBorder.MouseEnter += (s, e) =>
            {
                if (!_isDraggingCard && !IsSelectedOrAncestor(node))
                {
                    shadowBorder.Effect = hoverShadow;
                    cardBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
                }
            };
            cardBorder.MouseLeave += (s, e) =>
            {
                if (!_isDraggingCard && !IsSelectedOrAncestor(node))
                {
                    shadowBorder.Effect = shadow;
                    cardBorder.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFrom(GetNodeBorderBrush(node))!;
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
                        FindVisualParent<TextBox>(dep) != null ||
                        (FindVisualParent<Border>(dep)?.Tag as string == "DwgEditor"))
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
                cardBorder.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(GetNodeBackground(node))!;
                cardBorder.BorderBrush = isHilite ? new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)) : (SolidColorBrush)new BrushConverter().ConvertFrom(GetNodeBorderBrush(node))!;
            };

            cardBorder.Drop += (s, e) =>
            {
                bool isHilite = IsSelectedOrAncestor(node);
                cardBorder.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(GetNodeBackground(node))!;
                cardBorder.BorderBrush = isHilite ? new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)) : (SolidColorBrush)new BrushConverter().ConvertFrom(GetNodeBorderBrush(node))!;

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
                        FindVisualParent<TextBox>(dep) != null ||
                        (FindVisualParent<Border>(dep)?.Tag as string == "DwgEditor"))
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
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            badgeBorder.Child = badgeText;
            DockPanel.SetDock(badgeBorder, Dock.Left);
            topPanel.Children.Add(badgeBorder);

            // Isolate Eye Button (👁️ 눈 뜬 눈알 / 😌 눈 감은 눈알) on far Right
            bool isRootNode = (node.TreeDepth == 0);
            bool isEyeOpen = isRootNode || item.IsOpaque;

            var btnIsolate = new Button
            {
                Width = 24,
                Height = 24,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = isRootNode ? "클릭 시 전체 부품 모두 표시(불투명 복원)" : (isEyeOpen ? "눈 뜬 상태(표시 중): 클릭 시 이 항목만 격리 표시" : "눈 감은 상태(숨김): 클릭 시 전체 다시 표시")
            };

            btnIsolate.Content = CreateEyeIcon(isEyeOpen);

            btnIsolate.Click += (s, e) =>
            {
                e.Handled = true;

                if (isRootNode)
                {
                    // Root에 있는 원은 항상 초록색이며, 누르면 모두 표시
                    foreach (var raw in _rawItems)
                    {
                        raw.IsOpaque = true;
                    }
                    if (_swService != null)
                    {
                        _swService.ShowAllOpaque(_rawItems);
                    }
                }
                else
                {
                    bool isAllGreen = _rawItems.All(r => r.IsOpaque);

                    if (isAllGreen)
                    {
                        // 1. All 초록색 상태 -> 클릭한 항목만 격리 표시
                        if (_swService != null)
                        {
                            _swService.SetComponentsTransparency(new List<BOMItem> { item }, _rawItems, isolateMode: true);
                        }
                        else
                        {
                            var subDescendants = new HashSet<BOMItem>();
                            CollectDescendantItems(node, subDescendants);
                            subDescendants.Add(item);
                            foreach (var raw in _rawItems)
                            {
                                raw.IsOpaque = subDescendants.Contains(raw);
                            }
                        }
                    }
                    else
                    {
                        // 2. 이미 격리된 상태에서 초록색 항목을 다시 누르면 -> 전체 다 표시
                        if (item.IsOpaque)
                        {
                            foreach (var raw in _rawItems)
                            {
                                raw.IsOpaque = true;
                            }
                            if (_swService != null)
                            {
                                _swService.ShowAllOpaque(_rawItems);
                            }
                        }
                        else
                        {
                            // 빨간색 항목을 누르면 -> 해당 항목으로 새로 격리
                            if (_swService != null)
                            {
                                _swService.SetComponentsTransparency(new List<BOMItem> { item }, _rawItems, isolateMode: true);
                            }
                            else
                            {
                                var subDescendants = new HashSet<BOMItem>();
                                CollectDescendantItems(node, subDescendants);
                                subDescendants.Add(item);
                                foreach (var raw in _rawItems)
                                {
                                    raw.IsOpaque = subDescendants.Contains(raw);
                                }
                            }
                        }
                    }
                }

                RedrawTree();
                HierarchyChanged?.Invoke(this, EventArgs.Empty);
            };

            DockPanel.SetDock(btnIsolate, Dock.Right);
            topPanel.Children.Add(btnIsolate);

            // Sub1/Sub-Assy (excluding Root) & Part Dropdown Controls (Only active in Step 1: Assy 정리)
            if (_currentStep == BomProcessStep.Step1Assy)
            {
                if (node.TreeDepth > 0 && node.IsSubassembly)
                {
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
                        cboAssy.Items.Add(new ComboBoxItem { Content = "FRAME ASSY", Tag = "FRAME ASSY" });
                        cboAssy.Items.Add(new ComboBoxItem { Content = "BOTTOM COVER ASSY", Tag = "BOTTOM COVER ASSY" });
                    }
                    else
                    {
                        cboAssy.Items.Add(new ComboBoxItem { Content = "LID ASSY", Tag = "LID ASSY" });
                        cboAssy.Items.Add(new ComboBoxItem { Content = "ELASTOMER ASSY", Tag = "ELASTOMER ASSY" });
                        cboAssy.Items.Add(new ComboBoxItem { Content = "BSS ASSY", Tag = "BSS ASSY" });
                    }
                    cboAssy.Items.Add(new ComboBoxItem { Content = "Etc.", Tag = "Etc." });

                    string currentCat = (item.AssyCategory ?? "").Trim();
                    if (string.IsNullOrEmpty(currentCat))
                    {
                        currentCat = (item.PartName ?? "").Trim();
                    }
                    if (string.IsNullOrEmpty(currentCat) && !string.IsNullOrEmpty(item.FileName))
                    {
                        try { currentCat = System.IO.Path.GetFileNameWithoutExtension(item.FileName); } catch { }
                    }
                    if (string.IsNullOrEmpty(currentCat) && !string.IsNullOrEmpty(item.FilePath))
                    {
                        try { currentCat = System.IO.Path.GetFileNameWithoutExtension(item.FilePath); } catch { }
                    }

                    string NormalizeKey(string s) => (s ?? "").Replace(" ", "").Replace(".", "").Replace("_", "").Replace("-", "").ToUpperInvariant();
                    string normTarget = NormalizeKey(currentCat);

                    int selectedIdx = 0;
                    if (!string.IsNullOrEmpty(normTarget))
                    {
                        for (int i = 1; i < cboAssy.Items.Count; i++)
                        {
                            if (cboAssy.Items[i] is ComboBoxItem cbi)
                            {
                                string tag = (cbi.Tag as string ?? "").Trim();
                                string content = (cbi.Content as string ?? "").Trim();
                                if (NormalizeKey(tag) == normTarget || NormalizeKey(content) == normTarget)
                                {
                                    selectedIdx = i;
                                    break;
                                }
                            }
                        }

                        if (selectedIdx == 0)
                        {
                            for (int i = 1; i < cboAssy.Items.Count; i++)
                            {
                                if (cboAssy.Items[i] is ComboBoxItem cbi)
                                {
                                    string tag = (cbi.Tag as string ?? "").Trim();
                                    string content = (cbi.Content as string ?? "").Trim();
                                    string nTag = NormalizeKey(tag);
                                    string nCont = NormalizeKey(content);
                                    if ((!string.IsNullOrEmpty(nTag) && (nTag.Contains(normTarget) || normTarget.Contains(nTag))) ||
                                        (!string.IsNullOrEmpty(nCont) && (nCont.Contains(normTarget) || normTarget.Contains(nCont))))
                                    {
                                        selectedIdx = i;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    cboAssy.SelectedIndex = selectedIdx;
                    if (selectedIdx > 0 && cboAssy.Items[selectedIdx] is ComboBoxItem matchedItem)
                    {
                        string matchedTag = matchedItem.Tag as string ?? "";
                        if (string.IsNullOrEmpty(item.AssyCategory) && !string.IsNullOrEmpty(matchedTag))
                        {
                            item.AssyCategory = matchedTag;
                        }
                    }

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
                                    item.IsExpanded = true;
                                }
                                else
                                {
                                    node.IsExpanded = false;
                                    item.IsExpanded = false;
                                }
                                RedrawTree();
                                HierarchyChanged?.Invoke(this, EventArgs.Empty);
                            }
                        }
                    };

                    // Approval Checkbox (placed to the left of Isolate Circle button)
                    var chkApply = new CheckBox
                    {
                        Content = "승인",
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 4, 0),
                        Cursor = Cursors.Hand,
                        ToolTip = "체크 시 이 서브어셈블리를 승인(완료) 처리합니다.",
                        IsChecked = item.IsApproved
                    };

                    chkApply.Checked += (s, e) =>
                    {
                        item.IsApproved = true;
                        node.IsApproved = true;
                        node.IsExpanded = true;
                        item.IsExpanded = true;
                        if (string.IsNullOrEmpty(item.AssyCategory))
                        {
                            if (cboAssy.SelectedItem is ComboBoxItem selItem && !string.IsNullOrEmpty(selItem.Tag as string))
                            {
                                item.AssyCategory = selItem.Tag as string ?? "";
                            }
                            else if (cboAssy.Items.Count > 1)
                            {
                                cboAssy.SelectedIndex = 1;
                                item.AssyCategory = (cboAssy.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                            }
                        }
                        item.CheckModified();
                        RedrawTree();
                        HierarchyChanged?.Invoke(this, EventArgs.Empty);
                    };

                    chkApply.Unchecked += (s, e) =>
                    {
                        item.IsApproved = false;
                        node.IsApproved = false;
                        node.IsExpanded = false;
                        item.IsExpanded = false;
                        item.CheckModified();
                        RedrawTree();
                        HierarchyChanged?.Invoke(this, EventArgs.Empty);
                    };

                    DockPanel.SetDock(chkApply, Dock.Right);
                    topPanel.Children.Add(chkApply);
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
                        partOptions.Add(("FRAME", "FRAME"));
                        partOptions.Add(("FRAME BUSH", "FRAME BUSH"));
                        partOptions.Add(("FRAME BOLT", "FRAME bolt"));
                        partOptions.Add(("BUSH BOLT", "BUSH BOLT"));
                        partOptions.Add(("BOTTOM COVER", "Bottom Cover"));
                        partOptions.Add(("BLOCK", "BLOCK"));
                        partOptions.Add(("Etc.", "Etc."));
                    }
                    else if (parentAssyCat == "FRAME")
                    {
                        partOptions.Add(("[ 선택 ]", ""));
                        partOptions.Add(("FRAME", "FRAME"));
                        partOptions.Add(("FRAME BUSH", "FRAME BUSH"));
                        partOptions.Add(("BUSH BOLT", "BUSH BOLT"));
                        partOptions.Add(("BLOCK", "BLOCK"));
                        partOptions.Add(("Etc.", "Etc."));
                    }
                    else if (parentAssyCat == "BOTTOM")
                    {
                        partOptions.Add(("[ 선택 ]", ""));
                        partOptions.Add(("BOTTOM COVER", "Bottom Cover"));
                        partOptions.Add(("Etc.", "Etc."));
                    }
                    else if (parentAssyCat == "BSS")
                    {
                        partOptions.Add(("[ 선택 ]", ""));
                        partOptions.Add(("BSS BASE", "BSS BASE"));
                        partOptions.Add(("INSULATION FILM", "INSULATION FILM"));
                        partOptions.Add(("Etc.", "Etc."));
                    }
                    else if (parentAssyCat == "LID")
                    {
                        partOptions.Add(("[ 선택 ]", ""));
                        partOptions.Add(("COVER", "Cover"));
                        partOptions.Add(("COVER BOLT", "Cover bolt"));
                        partOptions.Add(("COVER SPRING", "Cover spring"));
                        partOptions.Add(("PUSHER", "Pusher"));
                        partOptions.Add(("PUSHER BOLT", "Pusher bolt"));
                        partOptions.Add(("PUSHER SPRING", "Pusher spring"));
                        partOptions.Add(("DIE PUSHER", "Die Pusher"));
                        partOptions.Add(("DIE PUSHER BOLT", "Die Pusher bolt"));
                        partOptions.Add(("DIE PUSHER SPRING", "Die Pusher spring"));
                        partOptions.Add(("LATCH", "Latch"));
                        partOptions.Add(("LEVER", "Lever"));
                        partOptions.Add(("INTERPOSER", "Interposer"));
                        partOptions.Add(("CAM", "Cam"));
                        partOptions.Add(("LID BOLT", "Lid bolt"));
                        partOptions.Add(("Etc.", "Etc."));
                    }
                    else if (parentAssyCat == "ROOT" || node.TreeDepth == 1)
                    {
                        partOptions.Add(("[ 선택 ]", ""));
                        partOptions.Add(("FRAME BOLT", "FRAME bolt"));
                        partOptions.Add(("DEVICE", "DEVICE"));
                        partOptions.Add(("PCB", "PCB"));
                        partOptions.Add(("Etc.", "Etc."));
                    }
                    else
                    {
                        partOptions.Add(("[ 선택 ]", ""));
                        partOptions.Add(("Etc.", "Etc."));
                    }

                    if (partOptions.Count > 0)
                    {
                        var chkPartApply = new CheckBox
                        {
                            Content = "승인",
                            FontSize = 12,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(4, 0, 4, 0),
                            Cursor = Cursors.Hand,
                            ToolTip = "체크 시 선택된 항목 승인",
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
                        if (!string.IsNullOrEmpty(currentCat))
                        {
                            for (int i = 1; i < cboPartCat.Items.Count; i++)
                            {
                                if (cboPartCat.Items[i] is ComboBoxItem cbi)
                                {
                                    string tag = (cbi.Tag as string ?? "").Trim();
                                    string content = (cbi.Content as string ?? "").Trim();
                                    if (string.Equals(tag, currentCat, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(content, currentCat, StringComparison.OrdinalIgnoreCase))
                                    {
                                        selectedIdx = i;
                                        break;
                                    }
                                }
                            }

                            if (selectedIdx == 0)
                            {
                                for (int i = 1; i < cboPartCat.Items.Count; i++)
                                {
                                    if (cboPartCat.Items[i] is ComboBoxItem cbi)
                                    {
                                        string tag = (cbi.Tag as string ?? "").Trim();
                                        string content = (cbi.Content as string ?? "").Trim();
                                        if (tag.IndexOf(currentCat, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            currentCat.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            selectedIdx = i;
                                            break;
                                        }
                                    }
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
                            item.IsApproved = true;
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
                            item.IsApproved = false;
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
            }

            Grid.SetRow(topPanel, 0);
            mainGrid.Children.Add(topPanel);

            // Row 1: Part Name (Left) & Material Dropdown (Right in Step 2)
            var row1Panel = new Grid();
            row1Panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Col 0: Part Name
            if (_currentStep == BomProcessStep.Step2DrawingNo && node.TreeDepth > 0)
            {
                row1Panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Col 1: Material Dropdown
            }

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
                Margin = new Thickness(2, 2, 6, 2),
                ToolTip = item.PartName
            };
            Grid.SetColumn(txtPartName, 0);
            row1Panel.Children.Add(txtPartName);

            if (_currentStep == BomProcessStep.Step2DrawingNo && node.TreeDepth > 0)
            {
                // 재질 드롭다운을 파트명 텍스트 오른쪽으로 배치
                var cboMat = new ComboBox
                {
                    Width = 115,
                    Height = 24,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    IsEditable = true,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Padding = new Thickness(4, 0, 4, 0),
                    Margin = new Thickness(4, 0, 2, 0),
                    ToolTip = "재질(Material) 선택 또는 직접 입력",
                    Cursor = Cursors.Hand
                };

                var matPresets = new List<string>
                {
                    "[재질]", "SUS", "SUS304", "AL60", "AL6061", "ULTEM 2300", "Kapton Film", "PEEK", "POM", "MC Nylon", "SKD11", "S45C", "SS400", "Brass", "Rubber", "Etc."
                };

                if (_rawItems != null)
                {
                    foreach (var raw in _rawItems)
                    {
                        if (!string.IsNullOrWhiteSpace(raw.Material) && !matPresets.Contains(raw.Material, StringComparer.OrdinalIgnoreCase))
                        {
                            matPresets.Add(raw.Material.Trim());
                        }
                    }
                }

                foreach (var mat in matPresets)
                {
                    cboMat.Items.Add(new ComboBoxItem { Content = mat, Tag = (mat == "[재질]" ? "" : mat) });
                }

                string curMat = (item.Material ?? "").Trim();
                int matchIdx = -1;
                for (int i = 0; i < cboMat.Items.Count; i++)
                {
                    if (cboMat.Items[i] is ComboBoxItem cbi)
                    {
                        string tag = cbi.Tag as string ?? "";
                        string content = cbi.Content as string ?? "";
                        if (string.Equals(tag, curMat, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(content, curMat, StringComparison.OrdinalIgnoreCase))
                        {
                            matchIdx = i;
                            break;
                        }
                    }
                }

                if (matchIdx >= 0)
                {
                    cboMat.SelectedIndex = matchIdx;
                }
                else
                {
                    cboMat.Text = curMat;
                }

                cboMat.SelectionChanged += (s, e) =>
                {
                    if (cboMat.SelectedItem is ComboBoxItem sel)
                    {
                        string val = sel.Tag as string ?? sel.Content as string ?? "";
                        if (val != item.Material)
                        {
                            item.Material = val;
                            item.CheckModified();
                            HierarchyChanged?.Invoke(this, EventArgs.Empty);
                        }
                    }
                };

                cboMat.LostFocus += (s, e) =>
                {
                    string typed = cboMat.Text.Trim();
                    if (typed != item.Material)
                    {
                        item.Material = typed;
                        item.CheckModified();
                        HierarchyChanged?.Invoke(this, EventArgs.Empty);
                    }
                };

                Grid.SetColumn(cboMat, 1);
                row1Panel.Children.Add(cboMat);
            }

            Grid.SetRow(row1Panel, 1);
            mainGrid.Children.Add(row1Panel);

            // Row 2: Middle Details or Drawing No. Input
            if (_currentStep == BomProcessStep.Step2DrawingNo && node.TreeDepth > 0)
            {
                // [Step 2] 파트명 아래 도면번호 입력칸 (100% 폭, 2pt 확대)
                var dwgContainer = new StackPanel
                {
                    Margin = new Thickness(2, 3, 2, 3),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var inputRow = new Grid();
                inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Col 0: "도번:" 레이블
                inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Col 1: 도번 입력/표시칸 (전체 채움)

                var lblDwg = new TextBlock
                {
                    Text = "도번:",
                    FontSize = 15, // 2pt 확대 (13 -> 15)
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                Grid.SetColumn(lblDwg, 0);
                inputRow.Children.Add(lblDwg);

                var dwgHost = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(dwgHost, 1);

                // 1. 컬러 파싱된 도면번호 텍스트 표시 영역 (평상시 / 다른 곳 클릭 시 표시)
                var dwgDisplayBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    MinHeight = 32, // 크기 2pt/4px 확대 (28 -> 32)
                    Padding = new Thickness(8, 4, 8, 4),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = Cursors.IBeam,
                    ToolTip = "클릭하여 도면번호 편집 (OOO-PPPPPGBBBXXXX)",
                    Tag = "DwgEditor"
                };

                var tbColorDwg = new TextBlock
                {
                    FontFamily = new FontFamily("Consolas, Lucida Console, Segoe UI, Malgun Gothic"),
                    FontSize = 15.5, // 2pt 확대 (13.5 -> 15.5)
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                dwgDisplayBorder.Child = tbColorDwg;

                // 2. 도면번호 편집 텍스트박스 (클릭 시 활성화)
                var txtDwg = new TextBox
                {
                    Text = item.DrawingNo ?? "",
                    Height = 32, // 크기 2pt/4px 확대 (28 -> 32)
                    FontSize = 15.5, // 2pt 확대 (13.5 -> 15.5)
                    FontFamily = new FontFamily("Consolas, Lucida Console, Segoe UI, Malgun Gothic"),
                    FontWeight = FontWeights.Bold,
                    Padding = new Thickness(7, 4, 7, 4),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
                    BorderThickness = new Thickness(1.5),
                    ToolTip = "도면번호 입력 후 다른 곳을 클릭하거나 Enter를 누르면 컬러 파싱 서식으로 적용됩니다.",
                    Visibility = Visibility.Collapsed
                };

                void UpdateDwgColorDisplay()
                {
                    tbColorDwg.Inlines.Clear();
                    var parsed = item.ParseDrawingNo();
                    if (!string.IsNullOrEmpty(parsed.O))
                    {
                        tbColorDwg.Inlines.Add(new Run(parsed.O) { Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)) });
                        if (!string.IsNullOrEmpty(parsed.Hyphen))
                            tbColorDwg.Inlines.Add(new Run(parsed.Hyphen) { Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)) });
                        if (!string.IsNullOrEmpty(parsed.P))
                            tbColorDwg.Inlines.Add(new Run(parsed.P) { Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06)) });
                        if (!string.IsNullOrEmpty(parsed.G))
                            tbColorDwg.Inlines.Add(new Run(parsed.G) { Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)) });
                        if (!string.IsNullOrEmpty(parsed.B))
                            tbColorDwg.Inlines.Add(new Run(parsed.B) { Foreground = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)) });
                        if (!string.IsNullOrEmpty(parsed.X))
                            tbColorDwg.Inlines.Add(new Run(parsed.X) { Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0x33, 0xEA)) });
                    }
                    else if (!string.IsNullOrWhiteSpace(item.DrawingNo))
                    {
                        tbColorDwg.Inlines.Add(new Run(item.DrawingNo) { Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)) });
                    }
                    else
                    {
                        tbColorDwg.Inlines.Add(new Run("(도번 입력)") { Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)), FontStyle = FontStyles.Italic, FontWeight = FontWeights.Normal });
                    }
                }

                UpdateDwgColorDisplay();

                void ApplyDwgEdit()
                {
                    if (txtDwg.Text != item.DrawingNo)
                    {
                        item.DrawingNo = txtDwg.Text;
                        item.CheckModified();
                        HierarchyChanged?.Invoke(this, EventArgs.Empty);
                    }
                    UpdateDwgColorDisplay();
                    txtDwg.Visibility = Visibility.Collapsed;
                    dwgDisplayBorder.Visibility = Visibility.Visible;

                    // Update card background according to whether drawing number is entered
                    cardBorder.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(GetNodeBackground(node))!;
                    if (!IsSelectedOrAncestor(node))
                    {
                        cardBorder.BorderBrush = (SolidColorBrush)new BrushConverter().ConvertFrom(GetNodeBorderBrush(node))!;
                    }
                }

                // 클릭 시 편집창으로 전환
                dwgDisplayBorder.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    dwgDisplayBorder.Visibility = Visibility.Collapsed;
                    txtDwg.Visibility = Visibility.Visible;
                    txtDwg.Text = item.DrawingNo ?? "";
                    txtDwg.Focus();
                    txtDwg.SelectAll();
                };

                // 입력 후 다른 곳을 클릭하면(LostFocus) 컬러 파싱 텍스트로 적용 및 전환
                txtDwg.LostFocus += (s, e) =>
                {
                    ApplyDwgEdit();
                };

                // Enter 키 입력 시 즉시 확정
                txtDwg.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        e.Handled = true;
                        ApplyDwgEdit();
                        cardBorder.Focus();
                    }
                    else if (e.Key == Key.Escape)
                    {
                        e.Handled = true;
                        txtDwg.Text = item.DrawingNo ?? "";
                        UpdateDwgColorDisplay();
                        txtDwg.Visibility = Visibility.Collapsed;
                        dwgDisplayBorder.Visibility = Visibility.Visible;
                        cardBorder.Focus();
                    }
                };

                dwgHost.Children.Add(dwgDisplayBorder);
                dwgHost.Children.Add(txtDwg);
                inputRow.Children.Add(dwgHost);
                dwgContainer.Children.Add(inputRow);

                Grid.SetRow(dwgContainer, 2);
                mainGrid.Children.Add(dwgContainer);
            }
            else
            {
                // [Step 1] Middle Details (DrawingNo, Material, Explanation)
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
            }

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

            if (item.IsUserCreated)
            {
                var createdBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xFF)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(6, 0, 0, 0),
                    Child = new TextBlock
                    {
                        Text = "생성됨",
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x43, 0x38, 0xCA))
                    }
                };
                leftInfoPanel.Children.Add(createdBadge);
            }
            else if (item.IsModified)
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

            // Subassembly Child Count Badge (Static display only)
            if (node.HasChildren)
            {
                var countBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 1.5, 6, 1.5),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = $"하위 {node.Children.Count}개 부품/어셈블리"
                };

                var txtCount = new TextBlock
                {
                    Text = $"{node.Children.Count}개",
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                countBadge.Child = txtCount;

                DockPanel.SetDock(countBadge, Dock.Right);
                bottomPanel.Children.Add(countBadge);
            }

            Grid.SetRow(bottomPanel, 3);
            mainGrid.Children.Add(bottomPanel);

            cardBorder.Child = mainGrid;
            return cardContainer;
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
                if (child is FrameworkElement elem && elem.Tag is BOMTreeNode n)
                {
                    bool isHighlighted = IsSelectedOrAncestor(n);
                    if (elem is Grid g)
                    {
                        var borders = g.Children.OfType<Border>().ToList();
                        if (borders.Count >= 2)
                        {
                            var sBorder = borders[0];
                            var cBorder = borders[1];

                            sBorder.Effect = isHighlighted ? (DropShadowEffect)Resources["HoverShadow"] : (DropShadowEffect)Resources["CardShadow"];

                            cBorder.BorderThickness = new Thickness(isHighlighted ? 3.75 : (n.TreeDepth == 0 ? 3.0 : 2.25));
                            cBorder.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(GetNodeBackground(n))!;
                            cBorder.BorderBrush = isHighlighted ? new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)) : (SolidColorBrush)new BrushConverter().ConvertFrom(GetNodeBorderBrush(n))!;
                        }
                    }
                    else if (elem is Border b)
                    {
                        b.BorderThickness = new Thickness(isHighlighted ? 3.75 : (n.TreeDepth == 0 ? 3.0 : 2.25));
                        b.Background = (SolidColorBrush)new BrushConverter().ConvertFrom(GetNodeBackground(n))!;
                        b.BorderBrush = isHighlighted ? new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)) : (SolidColorBrush)new BrushConverter().ConvertFrom(GetNodeBorderBrush(n))!;
                        b.Effect = isHighlighted ? (DropShadowEffect)Resources["HoverShadow"] : (DropShadowEffect)Resources["CardShadow"];
                    }
                }
            }
        }

        private string GetNodeBackground(BOMTreeNode node)
        {
            if (_currentStep == BomProcessStep.Step2DrawingNo)
            {
                if (node.TreeDepth == 0) return "#ECFDF5";
                if (!string.IsNullOrWhiteSpace(node.Item.DrawingNo))
                {
                    return "#ECFDF5";
                }
                return "#FFFFFF"; // Initial / Unfilled state in Step 2
            }
            return node.NodeBackground;
        }

        private string GetNodeBorderBrush(BOMTreeNode node)
        {
            if (_currentStep == BomProcessStep.Step2DrawingNo)
            {
                if (node.TreeDepth == 0) return "#10B981";
                if (!string.IsNullOrWhiteSpace(node.Item.DrawingNo))
                {
                    return "#10B981";
                }
                return "#CBD5E1";
            }
            return node.NodeBorderBrush;
        }

        private bool IsSelectedOrAncestor(BOMTreeNode node)
        {
            if (_selectedNode == null) return false;
            if (_selectedNode == node) return true;
            return _selectedNode.IsDescendantOf(node);
        }

        private static void CollectDescendantItems(BOMTreeNode n, HashSet<BOMItem> set)
        {
            foreach (var child in n.Children)
            {
                set.Add(child.Item);
                CollectDescendantItems(child, set);
            }
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent) return parent;

                if (child is Visual || child is System.Windows.Media.Media3D.Visual3D)
                {
                    child = VisualTreeHelper.GetParent(child);
                }
                else if (child is FrameworkContentElement fce)
                {
                    child = fce.Parent;
                }
                else if (child is ContentElement ce)
                {
                    child = ContentOperations.GetParent(ce);
                }
                else
                {
                    child = LogicalTreeHelper.GetParent(child);
                }
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

        private void BtnApplyToFile_Click(object sender, RoutedEventArgs e)
        {
            ApplyToFileRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BtnDevTemp_Click(object sender, RoutedEventArgs e)
        {
            DevTempRequested?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
