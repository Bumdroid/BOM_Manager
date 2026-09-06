using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BOMManager.Models
{
    public class BOMItem : INotifyPropertyChanged
    {
        private int _itemNo = 1;
        private bool _isCommonPart = false;
        private string _partName = string.Empty;
        private string _drawingNo = string.Empty;
        private string _material = string.Empty;
        private int _qty = 1;
        private string _rev = string.Empty;
        private string _explanation = string.Empty;
        private string _remark = string.Empty;
        private string _fileName = string.Empty;
        private string _filePath = string.Empty;
        private string _configuration = "Default";
        private bool _isModified = false;
        private bool _isSuppressed = false;
        private bool _isVirtual = false;
        private bool _isOpaque = true;
        private bool _isSubassembly = false;
        private bool _isExpanded = true;
        private int _level = 0;

        // 원본 값 저장 (수정 여부 감지용)
        public bool? OriginalIsCommonPart { get; set; }
        public string? OriginalPartName { get; set; }
        public string? OriginalDrawingNo { get; set; }
        public string? OriginalMaterial { get; set; }
        public int? OriginalQty { get; set; }
        public string? OriginalRev { get; set; }
        public string? OriginalExplanation { get; set; }
        public string? OriginalRemark { get; set; }

        public int ItemNo
        {
            get => _itemNo;
            set { SetProperty(ref _itemNo, value); OnPropertyChanged(nameof(ItemNoDisplay)); }
        }

        public string ItemNoDisplay => IsModified ? $"*{_itemNo}" : _itemNo.ToString();

        public bool IsCommonPart
        {
            get => _isCommonPart;
            set
            {
                if (SetProperty(ref _isCommonPart, value))
                {
                    CheckModified();
                    OnPropertyChanged(nameof(IsCommonPartModified));
                }
            }
        }

        public string PartName
        {
            get => _partName;
            set
            {
                if (SetProperty(ref _partName, value))
                {
                    CheckModified();
                    OnPropertyChanged(nameof(PartNameDisplay));
                    OnPropertyChanged(nameof(IsPartNameModified));
                }
            }
        }

        public string DrawingNo
        {
            get => _drawingNo;
            set
            {
                // 사용자가 - 포함해서 입력하면 -는 제외
                string clean = value?.Replace("-", "").Trim() ?? string.Empty;
                if (SetProperty(ref _drawingNo, clean))
                {
                    CheckModified();
                    OnPropertyChanged(nameof(IsDrawingNoModified));
                    OnPropertyChanged(nameof(DrawingNoPartO));
                    OnPropertyChanged(nameof(DrawingNoHyphen));
                    OnPropertyChanged(nameof(DrawingNoPartP));
                    OnPropertyChanged(nameof(DrawingNoPartG));
                    OnPropertyChanged(nameof(DrawingNoPartB));
                    OnPropertyChanged(nameof(DrawingNoPartX));
                }
            }
        }

        // 도면번호 세그먼트 (OOO-PPPPPGBBBXXXX 컬러 서식 표시용)
        public string DrawingNoPartO => ParseDrawingNo().O;
        public string DrawingNoHyphen => ParseDrawingNo().Hyphen;
        public string DrawingNoPartP => ParseDrawingNo().P;
        public string DrawingNoPartG => ParseDrawingNo().G;
        public string DrawingNoPartB => ParseDrawingNo().B;
        public string DrawingNoPartX => ParseDrawingNo().X;

        public (string O, string Hyphen, string P, string G, string B, string X) ParseDrawingNo()
        {
            if (string.IsNullOrWhiteSpace(_drawingNo))
            {
                return ("", "", "", "", "", "");
            }

            // 하이픈이 제외된 상태 기준으로 파싱
            string clean = _drawingNo.Replace("-", "").Trim();
            if (clean.Length <= 3)
            {
                return (clean, "", "", "", "", "");
            }

            string o = clean.Substring(0, 3);
            string rem = clean.Substring(3);
            string p = rem.Length >= 5 ? rem.Substring(0, 5) : rem;
            string g = rem.Length >= 6 ? rem.Substring(5, 1) : "";
            string b = rem.Length >= 9 ? rem.Substring(6, 3) : (rem.Length > 6 ? rem.Substring(6) : "");
            string x = rem.Length > 9 ? rem.Substring(9) : "";
            return (o, "-", p, g, b, x);
        }

        public string Material
        {
            get => _material;
            set
            {
                if (SetProperty(ref _material, value))
                {
                    CheckModified();
                    OnPropertyChanged(nameof(IsMaterialModified));
                }
            }
        }

        public int Qty
        {
            get => _qty;
            set
            {
                if (SetProperty(ref _qty, Math.Max(1, value)))
                {
                    CheckModified();
                    OnPropertyChanged(nameof(IsQtyModified));
                }
            }
        }

        public string Rev
        {
            get => _rev;
            set
            {
                if (SetProperty(ref _rev, value))
                {
                    CheckModified();
                    OnPropertyChanged(nameof(IsRevModified));
                }
            }
        }

        // 설명충 (Explanation / Description)
        public string Explanation
        {
            get => _explanation;
            set
            {
                if (SetProperty(ref _explanation, value ?? string.Empty))
                {
                    CheckModified();
                    OnPropertyChanged(nameof(IsExplanationModified));
                }
            }
        }

        public string Remark
        {
            get => _remark;
            set
            {
                if (SetProperty(ref _remark, value))
                {
                    CheckModified();
                    OnPropertyChanged(nameof(IsRemarkModified));
                }
            }
        }

        public string FileName
        {
            get => _fileName;
            set => SetProperty(ref _fileName, value);
        }

        public string FilePath
        {
            get => _filePath;
            set => SetProperty(ref _filePath, value);
        }

        public string Configuration
        {
            get => _configuration;
            set => SetProperty(ref _configuration, value);
        }

        public bool IsModified
        {
            get => _isModified;
            set
            {
                if (SetProperty(ref _isModified, value))
                {
                    OnPropertyChanged(nameof(ItemNoDisplay));
                }
            }
        }

        public bool IsSuppressed
        {
            get => _isSuppressed;
            set => SetProperty(ref _isSuppressed, value);
        }

        public bool IsVirtual
        {
            get => _isVirtual;
            set => SetProperty(ref _isVirtual, value);
        }

        public bool IsOpaque
        {
            get => _isOpaque;
            set => SetProperty(ref _isOpaque, value);
        }

        public bool IsSubassembly
        {
            get => _isSubassembly;
            set
            {
                if (SetProperty(ref _isSubassembly, value))
                {
                    OnPropertyChanged(nameof(PartNameDisplay));
                    OnPropertyChanged(nameof(SubassemblyIndent));
                    OnPropertyChanged(nameof(PartPrefix));
                }
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value))
                {
                    OnPropertyChanged(nameof(PartNameDisplay));
                }
            }
        }

        public int Level
        {
            get => _level;
            set
            {
                if (SetProperty(ref _level, value))
                {
                    OnPropertyChanged(nameof(PartNameDisplay));
                    OnPropertyChanged(nameof(SubassemblyIndent));
                    OnPropertyChanged(nameof(PartPrefix));
                }
            }
        }

        // 서브어셈블리 삼각형 앞 선행 공백 (Level > 0일 때 Level * 3개 공백: Level 1=3개, Level 2=6개...)
        public string SubassemblyIndent
        {
            get
            {
                if (_isSubassembly && _level > 0)
                {
                    return new string(' ', _level * 3);
                }
                return string.Empty;
            }
        }

        // 일반 파트 선행 접두사 (Level >= 1일 때 Level * 3개 공백 + "└  ")
        public string PartPrefix
        {
            get
            {
                if (!_isSubassembly && _level > 0)
                {
                    return $"{new string(' ', _level * 3)}└  ";
                }
                return string.Empty;
            }
        }

        // 계층 들여쓰기 서식 적용된 표시용 파트명
        public string PartNameDisplay
        {
            get
            {
                if (_isSubassembly)
                {
                    string indent = _level > 0 ? new string(' ', _level * 3) : "";
                    string triangle = _isExpanded ? "▼ " : "▶ ";
                    return $"{indent}{triangle}{_partName}";
                }
                else
                {
                    if (_level > 0)
                    {
                        return $"{new string(' ', _level * 3)}└  {_partName}";
                    }
                    return _partName;
                }
            }
        }

        // 개별 셀 수정 여부
        public bool IsCommonPartModified => OriginalIsCommonPart.HasValue && _isCommonPart != OriginalIsCommonPart.Value;
        public bool IsPartNameModified => OriginalPartName != null && _partName != OriginalPartName;
        public bool IsDrawingNoModified => OriginalDrawingNo != null && _drawingNo != OriginalDrawingNo;
        public bool IsMaterialModified => OriginalMaterial != null && _material != OriginalMaterial;
        public bool IsQtyModified => OriginalQty.HasValue && _qty != OriginalQty.Value;
        public bool IsRevModified => OriginalRev != null && _rev != OriginalRev;
        public bool IsExplanationModified => OriginalExplanation != null && _explanation != OriginalExplanation;
        public bool IsRemarkModified => OriginalRemark != null && _remark != OriginalRemark;

        public BOMItem() { }

        public BOMItem(
            int itemNo,
            string partName,
            string material = "",
            int qty = 1,
            string remark = "",
            string filePath = "",
            bool isSubassembly = false,
            int level = 0,
            string drawingNo = "",
            string explanation = "")
        {
            _itemNo = itemNo;
            _partName = partName;
            _drawingNo = drawingNo?.Replace("-", "").Trim() ?? string.Empty;
            _material = material;
            _qty = qty;
            _remark = remark;
            _filePath = filePath;
            _fileName = !string.IsNullOrEmpty(filePath) ? System.IO.Path.GetFileName(filePath) : "";
            _isSubassembly = isSubassembly;
            _level = level;
            _explanation = explanation ?? string.Empty;

            SnapshotOriginalValues();
        }

        public void SnapshotOriginalValues()
        {
            OriginalIsCommonPart = _isCommonPart;
            OriginalPartName = _partName;
            OriginalDrawingNo = _drawingNo;
            OriginalMaterial = _material;
            OriginalQty = _qty;
            OriginalRev = _rev;
            OriginalExplanation = _explanation;
            OriginalRemark = _remark;
            CheckModified();
        }

        public void ResetToOriginal()
        {
            if (OriginalIsCommonPart.HasValue) IsCommonPart = OriginalIsCommonPart.Value;
            if (OriginalPartName != null) PartName = OriginalPartName;
            if (OriginalDrawingNo != null) DrawingNo = OriginalDrawingNo;
            if (OriginalMaterial != null) Material = OriginalMaterial;
            if (OriginalQty.HasValue) Qty = OriginalQty.Value;
            if (OriginalRev != null) Rev = OriginalRev;
            if (OriginalExplanation != null) Explanation = OriginalExplanation;
            if (OriginalRemark != null) Remark = OriginalRemark;
            IsModified = false;
        }

        public bool CheckModified()
        {
            bool modified = (OriginalIsCommonPart.HasValue && _isCommonPart != OriginalIsCommonPart.Value) ||
                            (OriginalPartName != null && _partName != OriginalPartName) ||
                            (OriginalDrawingNo != null && _drawingNo != OriginalDrawingNo) ||
                            (OriginalMaterial != null && _material != OriginalMaterial) ||
                            (OriginalQty.HasValue && _qty != OriginalQty.Value) ||
                            (OriginalRev != null && _rev != OriginalRev) ||
                            (OriginalExplanation != null && _explanation != OriginalExplanation) ||
                            (OriginalRemark != null && _remark != OriginalRemark);

            IsModified = modified;
            return modified;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
