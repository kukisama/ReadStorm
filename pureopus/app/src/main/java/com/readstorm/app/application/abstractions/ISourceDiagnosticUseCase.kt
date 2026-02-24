package com.readstorm.app.application.abstractions

import com.readstorm.app.domain.models.SourceDiagnosticResult

interface ISourceDiagnosticUseCase {
    suspend fun diagnose(sourceId: Int, testKeyword: String): SourceDiagnosticResult
}
