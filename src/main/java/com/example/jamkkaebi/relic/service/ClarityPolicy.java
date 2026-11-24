package com.example.jamkkaebi.relic.service;

import com.example.jamkkaebi.relic.config.ClarityProperties;
import com.example.jamkkaebi.relic.domain.Clarity;
import com.example.jamkkaebi.relic.domain.Phase;
import com.example.jamkkaebi.relic.domain.UserRelic;
import org.springframework.stereotype.Component;

import java.time.Duration;
import java.time.LocalDateTime;
import java.util.List;

/**
 * 선명도 계산. <b>저장된 상태가 아니라 선명도 기준 시각({@code clarityAnchorAt})에서 매번 파생한다</b>.
 *
 * <p>상태를 컬럼으로 들고 있으면 "7일이 지났는데 아직 선명"이 생길 수 있다.
 * 기준 시각은 마지막 정화 시각과 따로 둔다 — 복구 한 단계를 깨면 선명도가 한 칸 오르도록 기준 시각을 당겨
 * 저장한다({@link #anchorFor}). 시간은 전부 {@code GameClock} 이 주는 서버 시각으로 잰다.
 */
@Component
public class ClarityPolicy {

    private final ClarityProperties properties;

    public ClarityPolicy(ClarityProperties properties) {
        this.properties = properties;
    }

    // 현재 선명도. 아직 오염 관리를 마치지 않은 유물은 {@code null}
    public Clarity clarityOf(UserRelic userRelic, LocalDateTime now) {
        if (!userRelic.isCompleted()) {
            return null;
        }
        LocalDateTime anchor = anchorOf(userRelic);
        if (anchor == null) {
            return Clarity.CLEAR;
        }
        return clarityOf(anchor, notBeforeLastPurified(userRelic, now));
    }

    // 기준 시각만으로 계산한 선명도
    Clarity clarityOf(LocalDateTime clarityAnchorAt, LocalDateTime now) {
        Duration elapsed = Duration.between(clarityAnchorAt, now);
        if (elapsed.compareTo(properties.resealedAfter()) >= 0) {
            return Clarity.RESEALED;
        }
        if (elapsed.compareTo(properties.fadedAfter()) >= 0) {
            return Clarity.FADED;
        }
        return Clarity.CLEAR;
    }

    /**
     * 이대로 두면 선명도가 한 칸 떨어지는 시각(선명 → 흐려짐, 흐려짐 → 재봉인).
     * 이미 재봉인됐거나 미완료면 {@code null}.
     */
    public LocalDateTime nextChangeAt(UserRelic userRelic, LocalDateTime now) {
        LocalDateTime anchor = anchorOf(userRelic);
        if (!userRelic.isCompleted() || anchor == null) {
            return null;
        }
        return switch (clarityOf(userRelic, now)) {
            case CLEAR -> anchor.plus(properties.fadedAfter());
            case FADED -> anchor.plus(properties.resealedAfter());
            case RESEALED -> null;
        };
    }

    // 이대로 두면 재봉인되는 시각. 이미 재봉인됐거나 미완료면 {@code null}.
    public LocalDateTime resealAt(UserRelic userRelic, LocalDateTime now) {
        LocalDateTime anchor = anchorOf(userRelic);
        if (!userRelic.isCompleted() || anchor == null || clarityOf(userRelic, now) == Clarity.RESEALED) {
            return null;
        }
        return anchor.plus(properties.resealedAfter());
    }

    /**
     * 지금 이 선명도가 막 시작된 것으로 보는 기준 시각. 선명은 지금, 흐려짐은 지금 - 흐려짐 기간.
     * 흐려짐으로 오르면 재봉인까지 흐려짐 구간을 온전히 받는다.
     *
     * @throws IllegalArgumentException 재봉인 (복구로 재봉인이 되지는 않는다)
     */
    public LocalDateTime anchorFor(Clarity clarity, LocalDateTime now) {
        return switch (clarity) {
            case CLEAR -> now;
            case FADED -> now.minus(properties.fadedAfter());
            case RESEALED -> throw new IllegalArgumentException("복구로 오를 수 있는 선명도는 흐려짐과 선명뿐입니다.");
        };
    }

    /**
     * 선명으로 돌아가기까지 남은 페이즈(보통 난이도로 한 판씩 깰 때). 재봉인은 봉인 해제·기억 회복, 흐려짐은 기억 회복.
     * 선명하면 복구할 것이 없으므로 관리 판(오염 관리) 하나만 돌려준다.
     */
    public List<Phase> remainingPhases(Clarity clarity) {
        if (clarity == Clarity.CLEAR) {
            return List.of(Phase.GUARD);
        }
        return Phase.from(clarity.restoreFrom()).stream()
                .filter(phase -> !phase.isLast())
                .toList();
    }

    // 기준 시각을 채우기 전(마이그레이션 전)의 행은 마지막 정화 시각을 기준으로 본다.
    private static LocalDateTime anchorOf(UserRelic userRelic) {
        return userRelic.getClarityAnchorAt() != null ? userRelic.getClarityAnchorAt() : userRelic.getLastPurifiedAt();
    }

    /**
     * 지금이 마지막 정화보다 이르면 마지막 정화 시각을 지금으로 본다. 흐려짐으로 오른 정령은 기준 시각이 흐려짐 경계에
     * 딱 붙어 있어(마지막 정화 - 흐려짐 기간), 서버 시각이 조금만 뒤로 가도 선명으로 계산되기 때문이다.
     */
    private static LocalDateTime notBeforeLastPurified(UserRelic userRelic, LocalDateTime now) {
        LocalDateTime lastPurifiedAt = userRelic.getLastPurifiedAt();
        return lastPurifiedAt != null && now.isBefore(lastPurifiedAt) ? lastPurifiedAt : now;
    }
}
