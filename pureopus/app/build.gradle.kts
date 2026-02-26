plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.readstorm.app"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.readstorm.app"
        minSdk = 21
        targetSdk = 35
        versionCode = 10400
        versionName = "1.4.0"
    }

    signingConfigs {
        getByName("debug") {
            // Uses default debug keystore
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
            signingConfig = signingConfigs.getByName("debug")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    buildFeatures {
        viewBinding = true
    }
}

dependencies {
    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("com.google.android.material:material:1.12.0")
    implementation("androidx.constraintlayout:constraintlayout:2.2.1")
    implementation("androidx.recyclerview:recyclerview:1.4.0")
    implementation("androidx.viewpager2:viewpager2:1.1.0")
    implementation("androidx.fragment:fragment-ktx:1.8.6")
    implementation("androidx.lifecycle:lifecycle-viewmodel-ktx:2.8.7")
    implementation("androidx.lifecycle:lifecycle-livedata-ktx:2.8.7")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.7")
    implementation("androidx.swiperefreshlayout:swiperefreshlayout:1.1.0")

    // Network
    implementation("com.squareup.okhttp3:okhttp:4.12.0")

    // HTML Parsing (equivalent to AngleSharp)
    implementation("org.jsoup:jsoup:1.18.3")

    // JSON
    implementation("com.google.code.gson:gson:2.11.0")

    // SQLite
    implementation("androidx.sqlite:sqlite-ktx:2.4.0")

    // Coroutines
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.9.0")

    // Markdown rendering
    implementation("io.noties.markwon:core:4.6.2")
}
