package com.example.jamkkaebi.friend.domain;

import com.example.jamkkaebi.global.common.BaseTimeEntity;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Index;
import jakarta.persistence.Table;
import jakarta.persistence.UniqueConstraint;
import lombok.AccessLevel;
import lombok.Getter;
import lombok.NoArgsConstructor;

import java.time.LocalDate;

/**
 * 친구 전시관 방문 기록. 하루에 같은 친구는 한 행이다.
 *
 * <p>방문 보상은 <b>몇 명을 방문하든 하루 1회</b>
 */
@Entity
@Table(
        name = "visit_logs",
        uniqueConstraints = @UniqueConstraint(
                name = "uk_visit_logs_day", columnNames = {"visitor_id", "host_id", "visit_date"}),
        indexes = @Index(name = "idx_visit_logs_visitor_day", columnList = "visitor_id, visit_date")
)
@Getter
@NoArgsConstructor(access = AccessLevel.PROTECTED)
public class VisitLog extends BaseTimeEntity {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "visitor_id", nullable = false)
    private Long visitorId;

    @Column(name = "host_id", nullable = false)
    private Long hostId;

    @Column(name = "visit_date", nullable = false)
    private LocalDate visitDate;

    @Column(nullable = false)
    private boolean rewarded;

    @Column(name = "jeongseong_gained", nullable = false)
    private int jeongseongGained;

    private VisitLog(Long visitorId, Long hostId, LocalDate visitDate, boolean rewarded, int jeongseongGained) {
        this.visitorId = visitorId;
        this.hostId = hostId;
        this.visitDate = visitDate;
        this.rewarded = rewarded;
        this.jeongseongGained = jeongseongGained;
    }

    public static VisitLog rewarded(Long visitorId, Long hostId, LocalDate visitDate, int jeongseong) {
        return new VisitLog(visitorId, hostId, visitDate, true, jeongseong);
    }

    public static VisitLog withoutReward(Long visitorId, Long hostId, LocalDate visitDate) {
        return new VisitLog(visitorId, hostId, visitDate, false, 0);
    }
}
