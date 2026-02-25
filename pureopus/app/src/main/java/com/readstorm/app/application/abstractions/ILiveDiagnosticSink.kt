package com.readstorm.app.application.abstractions

interface ILiveDiagnosticSink {
    fun append(line: String)

    fun clear()
}
