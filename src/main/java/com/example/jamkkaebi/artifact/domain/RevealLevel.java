package com.example.jamkkaebi.artifact.domain;

/**
 * 정체성 공개 단계.
 *
 * <p><b>서버가 통제한다.</b> 도달하지 않은 단계의 이름은 응답에 싣지 않는다.
 *
 * <p>한 번 오른 공개 단계는 <b>선명도가 떨어져도 내려가지 않는다.</b> 전시관에서 실루엣으로 보이는
 * 것은 공개 단계가 아니라 선명도({@link Clarity})가 결정한다.
 */
public enum RevealLevel {

    SILHOUETTE, // 획득 직후 — 실루엣만
    ALIAS,      // 봉인 해제 클리어 — 시적 별칭 공개
    FORM,       // 기억 회복 클리어 — 도깨비 형태(흑백) 공개
    NAMED;      // 정령 수호 클리어 — 실제 이름·풀 컬러

    // 봉인 해제 클리어 여부
    public boolean showsAlias() {
        return ordinal() >= ALIAS.ordinal();
    }

    // 정령 수호 클리어 여부
    public boolean showsRealName() {
        return this == NAMED;
    }

    /**
     * 진행도로부터 공개 단계를 정한다.
     *
     * @param status       완료 여부
     * @param currentPhase 최초 정화에서 <b>다음에 할</b> 페이즈 ({@code COMPLETED} 면 무시)
     */
    public static RevealLevel of(ArtifactStatus status, Phase currentPhase) {
        if (status == ArtifactStatus.COMPLETED) {
            return NAMED;
        }
        return switch (currentPhase) {
            case UNSEAL -> SILHOUETTE;
            case RECALL -> ALIAS;
            case GUARD -> FORM;
        };
    }
}
