"""
BOM Manager V0.0 - SolidWorks 2021 Signature Theme
Professional Light Slate & SolidWorks Classic Blue Style
"""

SW_SIGNATURE_STYLE = """
/* Global Window Style - SolidWorks Professional Light Theme */
QWidget {
    background-color: #E8ECF1; /* SolidWorks Main Window Canvas Gray */
    color: #2E3440;            /* Dark Charcoal Text */
    font-family: 'Segoe UI', 'Malgun Gothic', 'Noto Sans KR', sans-serif;
    font-size: 13px;
    selection-background-color: #0284C7;
    selection-color: #FFFFFF;
}

/* Main Window */
QMainWindow {
    background-color: #E8ECF1;
}

/* Header & Panels */
QFrame#headerPanel {
    background-color: #F8FAFC;
    border: 1px solid #CBD5E1;
    border-bottom: 2px solid #0284C7; /* SolidWorks Accent Blue Border */
    padding: 8px 14px;
    border-radius: 6px;
}

QFrame#cardPanel {
    background-color: #F8FAFC;
    border: 1px solid #CBD5E1;
    border-radius: 6px;
    padding: 8px 12px;
}

/* Labels & Typography - All Transparent Background */
QLabel {
    background-color: transparent;
    color: #1E293B;
}

QLabel#titleLabel {
    background-color: transparent;
    font-size: 17px;
    font-weight: bold;
    color: #0D47A1; /* SolidWorks Navy/Blue */
}

QLabel#subtitleLabel {
    background-color: transparent;
    font-size: 11px;
    color: #64748B;
}

QLabel#badgeLabel {
    font-size: 11px;
    font-weight: bold;
    padding: 4px 10px;
    border-radius: 12px;
}

QLabel#statValue {
    background-color: transparent;
    font-size: 16px;
    font-weight: bold;
    color: #0F172A;
}

QLabel#statLabel {
    background-color: transparent;
    font-size: 11px;
    color: #64748B;
    font-weight: 600;
}

/* Table Widget - SolidWorks FeatureManager Style */
QTableWidget, QTableView {
    background-color: #FFFFFF;
    border: 1px solid #CBD5E1;
    border-radius: 4px;
    gridline-color: #E2E8F0;
    color: #1E293B;
    alternate-background-color: #F8FAFC;
    selection-background-color: #BAE6FD; /* Soft SW Blue selection */
    selection-color: #0C4A6E;
}

QTableWidget::item {
    padding: 5px 8px;
    border-bottom: 1px solid #F1F5F9;
}

QTableWidget::item:selected {
    background-color: #BAE6FD;
    color: #0C4A6E;
    font-weight: 600;
}

QTableWidget::item:hover {
    background-color: #F0F9FF;
}

QHeaderView::section {
    background: qlineargradient(x1:0, y1:0, x2:0, y2:1, stop:0 #F8FAFC, stop:1 #E2E8F0);
    color: #334155;
    padding: 7px;
    font-weight: bold;
    font-size: 12px;
    border: none;
    border-right: 1px solid #CBD5E1;
    border-bottom: 2px solid #0284C7;
}

/* Buttons - SolidWorks 3D/Flat Hybrid Button Style */
QPushButton {
    background: qlineargradient(x1:0, y1:0, x2:0, y2:1, stop:0 #FFFFFF, stop:1 #E2E8F0);
    color: #1E293B;
    border: 1px solid #CBD5E1;
    border-radius: 4px;
    padding: 6px 14px;
    font-weight: 600;
    min-height: 18px;
}

QPushButton:hover {
    background: qlineargradient(x1:0, y1:0, x2:0, y2:1, stop:0 #F0F9FF, stop:1 #BAE6FD);
    border-color: #0284C7;
    color: #0C4A6E;
}

QPushButton:pressed {
    background-color: #E2E8F0;
    border-color: #94A3B8;
}

QPushButton:disabled {
    background-color: #F1F5F9;
    color: #94A3B8;
    border-color: #E2E8F0;
}

/* Primary Action Button (Apply to SW) - SolidWorks Blue */
QPushButton#primaryButton {
    background: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #0284C7, stop:1 #0369A1);
    color: #FFFFFF;
    border: 1px solid #0369A1;
    font-weight: bold;
    font-size: 13px;
    padding: 7px 18px;
}

QPushButton#primaryButton:hover {
    background: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #0369A1, stop:1 #075985);
    border-color: #0C4A6E;
}

QPushButton#primaryButton:pressed {
    background-color: #0C4A6E;
}

/* Success / Export Button */
QPushButton#successButton {
    background: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #10B981, stop:1 #059669);
    color: #FFFFFF;
    border: 1px solid #047857;
    font-weight: bold;
}

QPushButton#successButton:hover {
    background: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #34D399, stop:1 #10B981);
    border-color: #059669;
}

/* Accent Tool Buttons */
QPushButton#accentButton {
    background: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #4F46E5, stop:1 #4338CA);
    color: #FFFFFF;
    border: 1px solid #3730A3;
    font-weight: 600;
}

QPushButton#accentButton:hover {
    background: qlineargradient(x1:0, y1:0, x2:0, y2:1, stop:0 #6366F1, stop:1 #4F46E5);
}

/* Input Fields & Combo Boxes */
QLineEdit, QComboBox, QSpinBox {
    background-color: #FFFFFF;
    border: 1px solid #CBD5E1;
    border-radius: 4px;
    padding: 5px 8px;
    color: #1E293B;
}

QLineEdit:focus, QComboBox:focus, QSpinBox:focus {
    border: 1px solid #0284C7;
    background-color: #F0F9FF;
}

QComboBox::drop-down {
    subcontrol-origin: padding;
    subcontrol-position: top right;
    width: 20px;
    border-left: 1px solid #CBD5E1;
}

QComboBox QAbstractItemView {
    background-color: #FFFFFF;
    border: 1px solid #CBD5E1;
    color: #1E293B;
    selection-background-color: #BAE6FD;
    selection-color: #0C4A6E;
}

/* CheckBox - Transparent Background */
QCheckBox {
    background-color: transparent;
    color: #334155;
    spacing: 7px;
    font-weight: 500;
}

QCheckBox::indicator {
    width: 17px;
    height: 17px;
    border: 1px solid #94A3B8;
    border-radius: 3px;
    background-color: #FFFFFF;
}

QCheckBox::indicator:checked {
    background-color: #0284C7;
    border-color: #0369A1;
}

/* ScrollBar */
QScrollBar:vertical {
    border: none;
    background: #F1F5F9;
    width: 10px;
    margin: 0px;
    border-radius: 5px;
}

QScrollBar::handle:vertical {
    background: #CBD5E1;
    min-height: 20px;
    border-radius: 5px;
}

QScrollBar::handle:vertical:hover {
    background: #94A3B8;
}

QScrollBar::horizontal {
    border: none;
    background: #F1F5F9;
    height: 10px;
    margin: 0px;
    border-radius: 5px;
}

QScrollBar::handle:horizontal {
    background: #CBD5E1;
    min-width: 20px;
    border-radius: 5px;
}

QScrollBar::handle:horizontal:hover {
    background: #94A3B8;
}

/* Status Bar */
QStatusBar {
    background-color: #DFE4EA;
    color: #334155;
    font-size: 12px;
    border-top: 1px solid #CBD5E1;
}
"""

MODERN_STYLE = SW_SIGNATURE_STYLE

COMMON_MATERIALS = [
    "AL 60",
    "SUS",
    "ULTEM 2300"
]
