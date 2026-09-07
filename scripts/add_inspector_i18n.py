# -*- coding: utf-8 -*-
import json, os

loc_dir = "GameData/KSPGMAT/loc"

new_entries = {
    "btn_api_inspector": {
        "zh": "📖 API & 参数字典",
        "en": "📖 API & Param Dictionary",
        "ru": "📖 Словарь API и параметров",
        "jp": "📖 API＆パラメータ辞書"
    },
    "modal_api_inspector_title": {
        "zh": "GMAT 官方原生 API 与全量动态参数全景字典",
        "en": "GMAT Native API & Dynamic Parameter Catalog",
        "ru": "Справочник API GMAT и динамических параметров",
        "jp": "GMAT公式API＆動的パラメータカタログ"
    },
    "inspector_stat_badge": {
        "zh": "{0} 物理参数 | {1} 官方API类 | {2} 源码方法 | {3} 扩展函数",
        "en": "{0} Params | {1} API Classes | {2} Methods | {3} Ext Functions",
        "ru": "{0} параметров | {1} классов | {2} методов | {3} функций",
        "jp": "{0} パラメータ | {1} クラス | {2} メソッド | {3} 関数"
    },
    "inspector_search_placeholder": {
        "zh": "输入参数名/类名/方法名/物理量搜索 (支持模糊匹配)...",
        "en": "Search parameter, class, method, or physics (fuzzy match)...",
        "ru": "Поиск параметров, классов, методов (нечеткий поиск)...",
        "jp": "パラメータ名・クラス・メソッド・物理量を検索..."
    },
    "inspector_tab_all": {
        "zh": "全部 ({0})",
        "en": "All ({0})",
        "ru": "Все ({0})",
        "jp": "すべて ({0})"
    },
    "inspector_tab_params": {
        "zh": "📊 物理参数 ({0})",
        "en": "📊 Physics Params ({0})",
        "ru": "📊 Физические параметры ({0})",
        "jp": "📊 物理パラメータ ({0})"
    },
    "inspector_tab_classes": {
        "zh": "🏛️ 官方类与方法 ({0})",
        "en": "🏛️ API Classes & Methods ({0})",
        "ru": "🏛️ Классы и методы API ({0})",
        "jp": "🏛️ 公式クラス・メソッド ({0})"
    },
    "inspector_tab_functions": {
        "zh": "🐍 Python/C++扩展 ({0})",
        "en": "🐍 Extensions & Functions ({0})",
        "ru": "🐍 Расширения и функции ({0})",
        "jp": "🐍 拡張＆関数 ({0})"
    },
    "inspector_filter_all_families": {
        "zh": "全部分类",
        "en": "All Families",
        "ru": "Все категории",
        "jp": "すべてのカテゴリ"
    },
    "inspector_results_count": {
        "zh": "找到 {0} 条匹配结果",
        "en": "Found {0} matches",
        "ru": "Найдено {0} совпадений",
        "jp": "{0} 件の一致"
    },
    "inspector_no_results": {
        "zh": "未找到匹配项，请尝试缩短或更换搜索关键字",
        "en": "No matches found. Try changing your search query.",
        "ru": "Совпадений не найдено. Измените поисковый запрос.",
        "jp": "一致する項目が見つかりませんでした。"
    },
    "inspector_select_hint": {
        "zh": "← 请从左侧列表中选择一项查看详细元数据、继承关系及原生源码实现",
        "en": "← Select an item from the left to view metadata and native source code",
        "ru": "← Выберите элемент слева для просмотра метаданных и исходного кода",
        "jp": "← 左側のリストから項目を選択してメタデータと実装コードを確認"
    },
    "inspector_badge_param": {
        "zh": "物理参数",
        "en": "PARAM",
        "ru": "ПАРАМЕТР",
        "jp": "パラメータ"
    },
    "inspector_badge_class": {
        "zh": "官方类",
        "en": "CLASS",
        "ru": "КЛАСС",
        "jp": "クラス"
    },
    "inspector_badge_method": {
        "zh": "原生方法",
        "en": "METHOD",
        "ru": "МЕТОД",
        "jp": "メソッド"
    },
    "inspector_badge_function": {
        "zh": "顶层函数",
        "en": "FUNCTION",
        "ru": "ФУНКЦИЯ",
        "jp": "関数"
    },
    "inspector_badge_user_function": {
        "zh": "用户函数",
        "en": "USER FUNC",
        "ru": "ПОЛЬЗ. ФУНКЦИЯ",
        "jp": "ユーザー関数"
    },
    "inspector_badge_tool_function": {
        "zh": "工具库函数",
        "en": "TOOL FUNC",
        "ru": "УТИЛИТА",
        "jp": "ツール関数"
    },
    "inspector_badge_extremum": {
        "zh": "极值事件",
        "en": "EXTREMUM",
        "ru": "ЭКСТРЕМУМ",
        "jp": "極値イベント"
    },
    "inspector_label_category": {
        "zh": "所属类别 / 族群: {0}",
        "en": "Family / Category: {0}",
        "ru": "Категория: {0}",
        "jp": "カテゴリ / グループ: {0}"
    },
    "inspector_label_unit": {
        "zh": "物理单位: {0}",
        "en": "Physics Unit: {0}",
        "ru": "Единицы: {0}",
        "jp": "物理単位: {0}"
    },
    "inspector_label_parent_class": {
        "zh": "所属类: {0}",
        "en": "Belongs To Class: {0}",
        "ru": "Принадлежит классу: {0}",
        "jp": "所属クラス: {0}"
    },
    "inspector_label_desc": {
        "zh": "功能说明与文档",
        "en": "Description & Documentation",
        "ru": "Описание и документация",
        "jp": "機能説明とドキュメント"
    },
    "inspector_label_impl": {
        "zh": "官方 API 原生源码实现 (Native Implementation)",
        "en": "Official API Native Implementation",
        "ru": "Официальная реализация API",
        "jp": "公式APIネイティブ実装コード"
    },
    "inspector_label_capabilities": {
        "zh": "支持的操作命令能力",
        "en": "Supported Capabilities",
        "ru": "Поддерживаемые возможности",
        "jp": "サポートされる機能"
    },
    "inspector_cap_achieve": {
        "zh": "✓ 可作为 Achieve 目标量",
        "en": "✓ Valid for Achieve Target",
        "ru": "✓ Допустимо для Achieve",
        "jp": "✓ Achieve 目標値として使用可能"
    },
    "inspector_cap_vary": {
        "zh": "✓ 可作为 Vary 决策变量",
        "en": "✓ Valid for Vary Variable",
        "ru": "✓ Допустимо для Vary",
        "jp": "✓ Vary 変数として使用可能"
    },
    "inspector_cap_report": {
        "zh": "✓ 可作为 Report/XYPlot 输出",
        "en": "✓ Valid for Report/Plot",
        "ru": "✓ Допустимо для Report/Plot",
        "jp": "✓ Report/XYPlot 出力可能"
    },
    "inspector_cap_stop": {
        "zh": "✓ 可作为 Propagate 停止条件",
        "en": "✓ Valid for Propagate Stop",
        "ru": "✓ Допустимо для Propagate Stop",
        "jp": "✓ Propagate 停止条件として使用可能"
    },
    "inspector_btn_copy_name": {
        "zh": "📋 复制标识名",
        "en": "📋 Copy Identifier",
        "ru": "📋 Копировать имя",
        "jp": "📋 識別名をコピー"
    },
    "inspector_btn_copy_impl": {
        "zh": "📋 复制完整源码",
        "en": "📋 Copy Source Code",
        "ru": "📋 Копировать код",
        "jp": "📋 実装コードをコピー"
    },
    "inspector_copied_toast": {
        "zh": "已复制到剪贴板！",
        "en": "Copied to clipboard!",
        "ru": "Скопировано в буфер обмена!",
        "jp": "クリップボードにコピーしました！"
    }
}

for lang in ["zh", "en", "ru", "jp"]:
    fpath = os.path.join(loc_dir, f"{lang}.json")
    with open(fpath, "r", encoding="utf-8") as f:
        data = json.load(f)
    for k, v in new_entries.items():
        data[k] = v[lang]
    with open(fpath, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print(f"Updated {lang}.json with {len(new_entries)} keys")
