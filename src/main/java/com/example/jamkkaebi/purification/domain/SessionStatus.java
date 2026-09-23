package com.example.jamkkaebi.purification.domain;

// 정화 세션의 수명
public enum SessionStatus {
    ISSUED,    // 발급됨 (결과 미보고)
    SETTLED,   // 정산 완료
    SUPERSEDED // 새 세션이 시작돼 무효
}
