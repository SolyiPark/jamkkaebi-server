package com.example.jamkkaebi.purification.domain;

/**
 * 정화 결과 화면의 주 버튼. 다음에 무엇을 할지는 서버가 정한다.
 */
public enum NextAction {
    NEXT_PHASE,       // 최초 정화의 다음 페이즈로
    CONTINUE_RESTORE, // 복구 순서가 남음
    DONE,             // 이번 정화로 끝
    RETRY             // 실패 — 무료로 재도전
}
