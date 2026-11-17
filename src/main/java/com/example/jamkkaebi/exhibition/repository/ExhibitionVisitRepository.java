package com.example.jamkkaebi.exhibition.repository;

import com.example.jamkkaebi.exhibition.domain.ExhibitionVisit;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.time.LocalDate;
import java.util.List;

public interface ExhibitionVisitRepository extends JpaRepository<ExhibitionVisit, Long> {

    boolean existsByUserIdAndVisitDate(Long userId, LocalDate visitDate);

    // 방문한 날들 (가까운 날부터). 같은 날은 한 행이라 날짜가 겹치지 않는다
    @Query("SELECT v.visitDate FROM ExhibitionVisit v WHERE v.userId = :userId AND v.visitDate >= :from "
            + "ORDER BY v.visitDate DESC")
    List<LocalDate> findVisitDates(@Param("userId") Long userId, @Param("from") LocalDate from);

    @Modifying(flushAutomatically = true)
    @Query("DELETE FROM ExhibitionVisit v WHERE v.visitDate < :before")
    int deleteOlderThan(@Param("before") LocalDate before);
}
