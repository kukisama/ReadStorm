# Keep fragments loaded via reflection in MainActivity.createTabFragment()
-keep class com.readstorm.app.ui.fragments.** { *; }

# Keep model fields used by JSON/Gson parsing
-keepclassmembers class com.readstorm.app.domain.models.** {
    <fields>;
}

# Keep source rule model classes to avoid parser/serializer breakages
-keep class com.readstorm.app.domain.models.FullBookSourceRule { *; }
-keep class com.readstorm.app.domain.models.RuleSearchSection { *; }
-keep class com.readstorm.app.domain.models.RuleBookSection { *; }
-keep class com.readstorm.app.domain.models.RuleTocSection { *; }
-keep class com.readstorm.app.domain.models.RuleChapterSection { *; }
